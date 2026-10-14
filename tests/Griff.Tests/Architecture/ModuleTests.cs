using Griff.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Tests.Architecture;

/// <summary>
/// Guards Griff's modules (see GameModule): each genre lives in a folder of its own under src/Griff/Modules, in a namespace
/// to match, registered once by its module class, and the core names none of them. A module plugs in through extension
/// points instead: its systems and their steps (GameModule.AddTo), components that draw themselves and say what they
/// send, and interfaces Griff's own components call (ICounterSource, IGridDriver, ITurnReady, IPlaytestReport).
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
