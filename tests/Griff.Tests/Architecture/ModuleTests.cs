using System.Reflection;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Tests.Architecture;

/// <summary>
/// Guards Griff's modules (see GameModule): each genre lives in a folder of its own under src/Griff/Modules, in a namespace
/// to match, registered once by its module class, and the core names none of them. A module plugs in through extension
/// points instead: its systems and their steps (GameModule.AddTo) and hooks (GameSystem's Draw and the small interfaces it opts into: IViewDrawer, ISeesThroughEyes, IHudLayout,
/// RunForPlayer, GridStepStarting, SceneEnding...), components that draw themselves and say what they send, and interfaces
/// Griff's own components call (ICounterSource, IGridDriver, ITurnReady, IPlaytestReport, IHearsMessages,
/// IClaimsNumberKeys). Modules don't name each other either, so each stands alone, and the hosts a game runs in (the web
/// and desktop players) name none. Each is mashup-ready too: it names actions and outputs, and what of it reads the
/// player's input takes it only with focus.
/// </summary>
public sealed class ModuleTests
{
    private const string ModulesNamespace = "Griff.Modules";

    [Fact]
    public void Core_NamesNoModule()
    {
        var (compilation, trees) = LayerTests.Core();
        var moduleTypes = trees.Where(IsModuleFile)
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Select(d => d.Identifier.Text))
            .ToHashSet();

        var named = new List<string>();
        foreach (var tree in trees.Where(t => t.FilePath.Length > 0 && !IsModuleFile(t)))
        {
            // Only names a module declares (or its namespace) need binding, which keeps the test quick.
            var candidates = tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>()
                .Where(n => moduleTypes.Contains(n.Identifier.Text) || n.Identifier.Text == "Modules")
                .ToList();
            if (candidates.Count == 0)
                continue;
            var model = compilation.GetSemanticModel(tree);
            foreach (var name in candidates)
            {
                var symbol = model.GetSymbolInfo(name).Symbol;
                var used = symbol is INamespaceSymbol ns ? ns : (ISymbol?)LayerTests.Used(symbol);
                if (used is not null && InModules(used))
                    named.Add($"{Path.GetFileName(tree.FilePath)}: {LayerTests.Name(used)}");
            }
        }

        Assert.True(named.Count == 0,
            "Griff's core names a module's types, which it should reach through an extension point (a GameSystem's hooks, " +
            "the interfaces Griff's components call, drawing and network attributes):\n" + string.Join("\n", named.Distinct().Order()));
    }

    // The hosts and libraries a game runs in (the web player, the desktop player, the platform, the scripting and graphics
    // libraries) name no module either: what one does for a module, it does through a hook any module can fill (such as
    // GameModule.AddToWarmUp), so a game with none of a module's parts runs the same.
    [Theory]
    [InlineData("Griff.Web")]
    [InlineData("Griff.Player")]
    [InlineData("Griff.Platform.Windows")]
    [InlineData("Griff.Scripting")]
    [InlineData("Griff.Graphics")]
    [InlineData("Griff.Graphics.OpenGL")]
    public void Host_NamesNoModule(string project)
    {
        var (core, coreTrees) = LayerTests.Core();
        var trees = LayerTests.Parse(Path.Combine(LayerTests.RepoRoot(), "src", project)).ToList();
        Assert.NotEmpty(trees);
        // Bound with the core's source, so its names resolve; what else the project uses (WPF, the browser) needn't.
        var compilation = core.AddSyntaxTrees(trees);
        var moduleTypes = coreTrees.Where(IsModuleFile)
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Select(d => d.Identifier.Text))
            .ToHashSet();

        var named = new List<string>();
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>()
                         .Where(n => moduleTypes.Contains(n.Identifier.Text) || n.Identifier.Text == "Modules"))
            {
                var symbol = model.GetSymbolInfo(name).Symbol;
                var used = symbol is INamespaceSymbol ns ? ns : (ISymbol?)LayerTests.Used(symbol);
                if (used is not null && InModules(used))
                    named.Add($"{Path.GetFileName(tree.FilePath)}: {LayerTests.Name(used)}");
            }
        }

        Assert.True(named.Count == 0,
            $"{project} names a module's types, which it should reach through a hook any module can fill:\n" + string.Join("\n", named.Distinct().Order()));
    }

    // A module stands alone: it builds on the core, not on another module, so any one can go without breaking the rest.
    [Fact]
    public void EachModule_NamesNoOtherModule()
    {
        var (compilation, trees) = LayerTests.Core();
        var named = new List<string>();
        foreach (var tree in trees.Where(IsModuleFile))
        {
            var own = $"{ModulesNamespace}.{FolderOf(tree)}";
            var model = compilation.GetSemanticModel(tree);
            foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var symbol = model.GetSymbolInfo(name).Symbol;
                var used = symbol is INamespaceSymbol ns ? ns : (ISymbol?)LayerTests.Used(symbol);
                if (used is null || !InModules(used))
                    continue;
                var where = used is INamespaceSymbol n ? n.ToDisplayString() : used.ContainingNamespace.ToDisplayString();
                if (where != ModulesNamespace && where != own && !where.StartsWith(own + ".", StringComparison.Ordinal))
                    named.Add($"{FolderOf(tree)}/{Path.GetFileName(tree.FilePath)}: {LayerTests.Name(used)}");
            }
        }

        Assert.True(named.Count == 0,
            "A module names another module's types; it should reach it through the core's extension points:\n" + string.Join("\n", named.Distinct().Order()));
    }

    [Fact]
    public void EachModule_LivesInItsOwnFolderAndNamespace_WithOneModuleClass()
    {
        var (compilation, trees) = LayerTests.Core();
        var folders = Directory.GetDirectories(Path.Combine(LayerTests.RepoRoot(), "src", "Griff", "Modules")).Select(Path.GetFileName).Order().ToList();
        var problems = new List<string>();

        foreach (var tree in trees.Where(IsModuleFile))
        {
            var folder = FolderOf(tree);
            var expected = $"{ModulesNamespace}.{folder}";
            foreach (var ns in tree.GetRoot().DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            {
                if (ns.Name.ToString() != expected)
                    problems.Add($"{folder}/{Path.GetFileName(tree.FilePath)} is in namespace {ns.Name}, not {expected}.");
            }
        }

        // Nothing outside the folders claims a module's namespace.
        foreach (var tree in trees.Where(t => t.FilePath.Length > 0 && !IsModuleFile(t)))
        {
            foreach (var ns in tree.GetRoot().DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Where(n => n.Name.ToString().StartsWith(ModulesNamespace, StringComparison.Ordinal)))
                problems.Add($"{Path.GetFileName(tree.FilePath)} is in {ns.Name} but outside src/Griff/Modules.");
        }

        // One module class in each folder, registered once (found, not listed).
        var gameModule = compilation.GetTypeByMetadataName("Griff.Runtime.GameModule")!;
        foreach (var folder in folders)
        {
            var modules = trees.Where(t => IsModuleFile(t) && FolderOf(t) == folder)
                .SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                    .Select(c => compilation.GetSemanticModel(t).GetDeclaredSymbol(c) as INamedTypeSymbol))
                .Where(c => c?.BaseType is { } b && SymbolEqualityComparer.Default.Equals(b, gameModule))
                .ToList();
            if (modules.Count != 1)
                problems.Add($"Modules/{folder} has {modules.Count} module classes; it needs exactly one.");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Equal(folders.Select(f => $"{ModulesNamespace}.{f}").Order(), GameModule.All.Select(m => m.Namespace).Order());
    }

    // Every genre is mashup-ready (the Mixing genres guide): its components name something they do and something they know,
    // so any other genre's pieces drive and read them with no code. A Focus action alone doesn't count: anything that takes
    // input gets one for free.
    [Fact]
    public void EachModule_NamesAnActionAndAnOutput()
    {
        var problems = new List<string>();
        foreach (var module in GameModule.All)
        {
            var components = ComponentsOf(module);
            if (!components.Any(t => ComponentActions.Of(t).Any(a => a.Method.DeclaringType != typeof(ComponentFocus))))
                problems.Add($"{module.Name} names no action: mark what one of its components does [Action] (a board's Restart, a weapon's Fire), so a trigger, a card or a wire can set it off.");
            if (!components.Any(t => ComponentOutputs.Of(t).Count > 0))
                problems.Add($"{module.Name} names no output: mark what one of its components knows [Output] (a board's Score, a unit's Moved), so a counter, a bar, a text or a wire can read it.");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // A genre's component that reads the player's input (it names an input action or a key) takes it only while it has focus,
    // so it shares a scene with another genre's: a property of its own marked [Focus], or [FocusFrom] what it goes with.
    [Fact]
    public void EachModule_TakesInputOnlyWithFocus()
    {
        var problems = new List<string>();
        foreach (var type in GameModule.All.SelectMany(ComponentsOf).Where(ReadsInput))
        {
            if (ComponentFocus.Has(type))
                continue;
            if (type.GetCustomAttribute<FocusFromAttribute>() is not { } from)
                problems.Add($"{type.FullName} reads the player's input but has no focus: give it a true-or-false Controllable marked [Focus], or mark it [FocusFrom] the component whose focus it goes by.");
            else if (!ComponentFocus.Has(from.Type))
                problems.Add($"{type.FullName} takes its focus from {from.Type.Name}, which has none.");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void ReadsInput_FindsInputActionsKeysAndRowsOfThem()
    {
        Assert.True(ReadsInput(typeof(Griff.Modules.Fighting.FightingCharacter)));
        Assert.True(ReadsInput(typeof(Griff.Modules.BlockWorlds.Crafting)));
        Assert.True(ReadsInput(typeof(Griff.Modules.Puzzles.MoveHistory)));
        Assert.False(ReadsInput(typeof(Griff.Modules.PhysicsGames.Bumper)));
    }

    // The component types a module declares.
    private static IEnumerable<Type> ComponentsOf(GameModule module) =>
        module.GetType().Assembly.GetTypes().Where(t => typeof(Component).IsAssignableFrom(t) && !t.IsAbstract &&
                                                        (t.Namespace == module.Namespace || t.Namespace?.StartsWith(module.Namespace + ".", StringComparison.Ordinal) == true));

    // It names an input action, a key or a button to press, or rows of them (a fighter's buttons).
    private static bool ReadsInput(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(p =>
            p.GetCustomAttribute<NamesAttribute>() is { Kind: NameKind.Action } ||
            p.PropertyType == typeof(Griff.Input.Key) || p.PropertyType == typeof(MouseButton) || p.PropertyType == typeof(GamepadButton) ||
            p.GetCustomAttribute<RowsAttribute>()?.Columns.Any(c => c.Names is { Kind: NameKind.Action }) == true);

    private static bool IsModuleFile(SyntaxTree tree) =>
        tree.FilePath.Length > 0 && Path.GetRelativePath(Path.Combine(LayerTests.RepoRoot(), "src", "Griff", "Modules"), tree.FilePath) is var relative &&
        !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);

    private static string FolderOf(SyntaxTree tree) =>
        Path.GetRelativePath(Path.Combine(LayerTests.RepoRoot(), "src", "Griff", "Modules"), tree.FilePath).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

    private static bool InModules(ISymbol symbol)
    {
        var ns = symbol is INamespaceSymbol self ? self.ToDisplayString() : symbol.ContainingNamespace?.ToDisplayString() ?? "";
        return ns == ModulesNamespace || ns.StartsWith(ModulesNamespace + ".", StringComparison.Ordinal);
    }
}
