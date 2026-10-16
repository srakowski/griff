using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Tests.Architecture;

/// <summary>
/// Guards what a module may use: only what a script's system could too. A module is code like any other that adds a
/// system to a game, so everything it asks of the game (<c>Game</c>, the components, the services around them) is public
/// and documented, or a system it asks for by type; nothing it uses is internal to Griff. When the modules move out of
/// the core's assembly this is what lets them.
/// </summary>
public sealed class ModuleApiTests
{
    [Fact]
    public void Modules_UseOnlyThePublicApiOfTheCore()
    {
        var (compilation, trees) = LayerTests.Core();
        var misuse = new List<string>();
        foreach (var tree in trees.Where(t => IsModuleFile(t.FilePath)))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                if (model.GetSymbolInfo(name).Symbol is not { } symbol || !IsInternalToTheCore(symbol))
                    continue;
                var line = name.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                misuse.Add($"{LayerTests.Name(symbol)}   ({Path.GetFileName(tree.FilePath)}:{line})");
            }
        }

        Assert.True(misuse.Count == 0,
            "A module may use only the core's public, documented API, as a script's system could (make the member public with " +
            "a /// summary, or move it to the system that owns it):\n" + string.Join("\n", misuse.GroupBy(m => m[..m.IndexOf("   (", StringComparison.Ordinal)]).Select(g => g.First()).Order().Take(200)) +
            (misuse.Count > 80 ? $"\n... and {misuse.Count - 80} more" : ""));
    }

    // Griff's steps are named by ids: a module can't name one that isn't there, and no two share an id.
    [Fact]
    public void Steps_AreDistinctMembersOfTheirPhase()
    {
        var steps = typeof(Griff.Runtime.Steps).GetFields().Select(f => (Griff.Runtime.StepId)f.GetValue(null)!).ToList();

        Assert.Equal(steps.Count, steps.Distinct().Count());
        Assert.Equal(steps.Count, steps.Select(s => s.ToString()).Distinct().Count());
    }

    // A step is placed by an id: one that is not a step of the frame (another game's, say) fails here, not when a game starts.
    [Fact]
    public void AStep_NamingAnIdThatIsNotInTheFrame_Fails()
    {
        var setup = new Griff.Runtime.GameSetup(new Griff.Runtime.Game(new Griff.Scenes.Scene(), new Griff.Runtime.InputState()));
        setup.Module = new Probe();
        var system = setup.AddSystem(new ProbeSystem());
        setup.StepAfter(new Griff.Runtime.StepId(Griff.Runtime.FramePhase.Logic, "Nowhere"), "Late", system, () => { });

        var error = Assert.Throws<InvalidOperationException>(() => setup.Place([]));

        Assert.Contains("isn't a step of this frame", error.Message);
    }

    // A module's system is scheduled by its steps alone, not by Update as well.
    [Fact]
    public void AModuleSystem_OverridingUpdate_IsRefused()
    {
        var setup = new Griff.Runtime.GameSetup(new Griff.Runtime.Game(new Griff.Scenes.Scene(), new Griff.Runtime.InputState()));

        Assert.Throws<InvalidOperationException>(() => setup.AddSystem(new UpdatingSystem()));
    }

    private sealed class Probe : Griff.Runtime.GameModule
    {
        public override string Name => "Probe";

        protected internal override void AddTo(Griff.Runtime.GameSetup game)
        {
        }
    }

    private sealed class ProbeSystem : Griff.Runtime.GameSystem;

    private sealed class UpdatingSystem : Griff.Runtime.GameSystem
    {
        protected override void Update()
        {
        }
    }

    private static bool IsModuleFile(string path) =>
        path.Replace('\\', '/').Contains("/src/Griff/Modules/", StringComparison.Ordinal);

    // A symbol of the game's API (everything in the core but the components' own data in Griff.Scenes, the project tooling
    // the templates use and the editing metadata) that isn't public: itself or a type it is in.
    private static bool IsInternalToTheCore(ISymbol symbol)
    {
        if (symbol is IMethodSymbol { ReducedFrom: { } extension })
            symbol = extension;
        if (symbol is ITypeParameterSymbol or IParameterSymbol or ILocalSymbol or IRangeVariableSymbol or ILabelSymbol or IAliasSymbol
            or INamespaceSymbol or IDiscardSymbol or IPreprocessingSymbol)
            return false;
        if (symbol.ContainingNamespace is not { } ns || !symbol.Locations.Any(l => l.IsInSource))
            return false;
        var name = ns.ToDisplayString();
        if (name.StartsWith("Griff.Modules", StringComparison.Ordinal) || name.StartsWith("Griff.Templates", StringComparison.Ordinal) || name.StartsWith("Griff.Editing", StringComparison.Ordinal) || name.StartsWith("Griff.Scenes", StringComparison.Ordinal))
            return false;
        for (var at = symbol; at is not null and not INamespaceSymbol; at = at.ContainingSymbol)
        {
            if (at.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedAndInternal)
                return true;
        }

        return false;
    }
}
