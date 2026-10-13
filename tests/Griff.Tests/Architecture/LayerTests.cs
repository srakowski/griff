using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Tests.Architecture;

/// <summary>
/// Guards the core's layers: Scenes is data (entities, components, serialization), Runtime simulates it and
/// Rendering draws it, so nothing in Griff.Scenes may use Griff.Runtime or Griff.Rendering. The uses that exist today
/// are listed below and may only shrink: a new one fails, and so does a listed one that has gone, so the list is
/// trimmed as they're fixed.
/// </summary>
public sealed class LayerTests
{
    // "<type in Griff.Scenes> -> <type it uses in Griff.Runtime or Griff.Rendering>"
    private static readonly string[] Allowed =
    [
        "Griff.Scenes.Button -> Griff.Runtime.Key",
        "Griff.Scenes.Crafting -> Griff.Runtime.Key",
        "Griff.Scenes.Follow -> Griff.Runtime.Box",
        "Griff.Scenes.Follow -> Griff.Runtime.Physics",
        "Griff.Scenes.GridMovement -> Griff.Rendering.SceneRenderer",
        "Griff.Scenes.Health -> Griff.Runtime.Behavior",
        "Griff.Scenes.Mover -> Griff.Runtime.Ease",
        "Griff.Scenes.MoveSnapshot -> Griff.Rendering.SceneRenderer",
        "Griff.Scenes.Multiplayer -> Griff.Runtime.NetSession",
        "Griff.Scenes.Room -> Griff.Runtime.Box",
        "Griff.Scenes.SandboxTools -> Griff.Runtime.Key",
        "Griff.Scenes.Scene -> Griff.Rendering.SceneRenderer",
        "Griff.Scenes.Scoreboard -> Griff.Runtime.Key",
        "Griff.Scenes.Shooter -> Griff.Runtime.Rand",
        "Griff.Scenes.Spawner -> Griff.Runtime.Game",
        "Griff.Scenes.Spawner -> Griff.Runtime.Rand",
        "Griff.Scenes.Trail -> Griff.Rendering.SceneRenderer",
    ];

    [Fact]
    public void Scenes_DoesNotUseRuntimeOrRendering()
    {
        var uses = ScenesUses().ToHashSet();

        var added = uses.Except(Allowed).Order().ToList();
        var gone = Allowed.Except(uses).Order().ToList();

        Assert.True(added.Count == 0,
            "Griff.Scenes is data and mustn't use Griff.Runtime or Griff.Rendering (pass what a component needs in, " +
            "or move the code up a layer):\n" + string.Join("\n", added));
        Assert.True(gone.Count == 0, "Fixed, so take these off LayerTests.Allowed:\n" + string.Join("\n", gone));
    }

    // Binds the core's source and finds every Runtime or Rendering symbol named inside a Griff.Scenes type.
    private static IEnumerable<string> ScenesUses()
    {
        var source = Path.Combine(RepoRoot(), "src", "Griff");
        var trees = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(source, path))
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest), path))
            .Append(CSharpSyntaxTree.ParseText(ImplicitUsings))
            .ToList();
        var compilation = CSharpCompilation.Create("Griff", trees, PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        // Anything unbound would hide a use; the core needs nothing beyond the platform.
        var unbound = compilation.GetDiagnostics().Where(d => d.Id is "CS0246" or "CS0234" or "CS0103").Take(5).ToList();
        Assert.True(unbound.Count == 0, string.Join("\n", unbound));

        foreach (var tree in trees.Where(DeclaresScenes))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                if (name.Ancestors().OfType<BaseTypeDeclarationSyntax>().LastOrDefault() is not { } declaration ||
                    model.GetDeclaredSymbol(declaration) is not { } user || !InLayer(user, "Griff.Scenes"))
                    continue;
                if (Used(model.GetSymbolInfo(name).Symbol) is { } used && (InLayer(used, "Griff.Runtime") || InLayer(used, "Griff.Rendering")))
                    yield return $"{Name(user)} -> {Name(used)}";
            }
        }
    }

    // Only these files need binding, which keeps the test quick.
    private static bool DeclaresScenes(SyntaxTree tree) =>
        tree.GetRoot().DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()
            .Any(n => n.Name.ToString() is var ns && (ns == "Griff.Scenes" || ns.StartsWith("Griff.Scenes.", StringComparison.Ordinal)));

    // The type a name refers to: the type itself, or the type that declares the member.
    private static INamedTypeSymbol? Used(ISymbol? symbol) => symbol switch
    {
        INamedTypeSymbol type => type.OriginalDefinition,
        IMethodSymbol { ReducedFrom: { } extension } => extension.ContainingType,
        { ContainingType: { } type } => type.OriginalDefinition,
        _ => null,
    };

    private static bool InLayer(ISymbol symbol, string layer)
    {
        var ns = symbol.ContainingNamespace.ToDisplayString();
        return ns == layer || ns.StartsWith(layer + ".", StringComparison.Ordinal);
    }

    private static string Name(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None));

    private static bool IsBuildOutput(string project, string path)
    {
        var relative = Path.GetRelativePath(project, path);
        return relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // What the SDK's ImplicitUsings adds for a plain library (Griff.csproj).
    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    private static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                       Path.GetFileName(path) is "mscorlib.dll" or "netstandard.dll")
        .Select(path => MetadataReference.CreateFromFile(path));

    internal static string RepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "griff.slnx")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new DirectoryNotFoundException("The tests run from inside the Griff repository.");
    }
}
