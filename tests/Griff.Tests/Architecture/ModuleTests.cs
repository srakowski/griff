using System.Reflection;
using System.Xml.Linq;
using Griff.Editing;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Tests.Architecture;

/// <summary>
/// Guards Griff's modules (see GameModule): each genre lives in a folder of its own under src/Modules, in a namespace to
/// match, its running parts an assembly (Griff.Modules.Match3) built on Griff alone and its editor parts another beside them
/// (Griff.Modules.Match3.Editor, in its Editor folder) built on its module and Griff.Editing, registered once by its module
/// class. The compiler keeps who may name whom: Griff references no module (nor the editor's parts), a module no other
/// module, and the hosts a game runs in (the web and desktop players) and the libraries they share none, so a module plugs
/// in through extension points instead: its systems and their steps (GameModule.AddTo) and hooks (GameSystem's Draw and
/// the small interfaces it opts into), components that draw themselves and say what they send, and interfaces Griff's own
/// components call. Each is mashup-ready too: it names actions and outputs, and what of it reads the player's input takes
/// it only with focus.
/// </summary>
public sealed class ModuleTests
{
    private const string ModulesNamespace = "Griff.Modules";

    // Griff is the bottom: it references no Griff assembly, so it can't name a module or the editor's parts (Editing,
    // Agent, templates), and grants no module its internals, so a module uses only its public API, as a script's system.
    [Fact]
    public void Griff_ReferencesNoModuleNorTheEditor_AndOpensItsInternalsToTestsAlone()
    {
        var griff = typeof(Game).Assembly;
        Assert.Empty(griff.GetReferencedAssemblies().Where(a => a.Name?.StartsWith("Griff", StringComparison.Ordinal) == true).Select(a => a.Name));
        Assert.Equal(["Griff.Tests"], griff.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>().Select(a => a.AssemblyName));
        Assert.Empty(XDocument.Load(Path.Combine(LayerTests.RepoRoot(), "src", "Griff", "Griff.csproj")).Descendants("ProjectReference"));
    }

    // A module stands alone: its running parts build on Griff, not on another module, so any one can go without breaking
    // the rest; its editor parts on its own module and Griff.Editing.
    [Fact]
    public void EachModule_BuildsOnGriffAlone_AndItsEditorPartsOnItAndGriffEditing()
    {
        var problems = new List<string>();
        Assert.Equal(GameModule.All.Count, EditorModules.All.Count);
        foreach (var module in GameModule.All)
        {
            var assembly = module.GetType().Assembly;
            var name = assembly.GetName().Name!;
            Assert.Equal(module.Namespace, name);
            Assert.True(ModuleAssemblies.IsModule(assembly));
            foreach (var used in GriffsReferences(assembly).Where(r => r != "Griff"))
                problems.Add($"{name} references {used}: a module builds on Griff alone.");

            var editor = EditorModules.All.SingleOrDefault(a => a.GetName().Name == name + ModuleAssemblies.EditorSuffix);
            if (editor is null)
            {
                problems.Add($"{name} has no editor parts beside it ({name}{ModuleAssemblies.EditorSuffix}).");
                continue;
            }

            Assert.False(ModuleAssemblies.IsModule(editor));
            foreach (var used in GriffsReferences(editor).Where(r => r is not ("Griff" or "Griff.Editing") && r != name))
                problems.Add($"{editor.GetName().Name} references {used}: a module's editor parts build on it and Griff.Editing alone.");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // The hosts and libraries a game runs in (the web player, the desktop player, the platform, the scripting, exporting,
    // graphics and font libraries) and the editor's own libraries (Editing, Agent) build on no module: what one does for a
    // module, it does through a hook any module can fill (such as GameModule.AddToWarmUp), so a game with none of a
    // module's parts runs the same, and an export carries only the modules its project uses.
    [Theory]
    [InlineData("src/Griff.Web/Griff.Web.csproj")]
    [InlineData("src/Griff.Player/Griff.Player.csproj")]
    [InlineData("src/Griff.Platform.Windows/Griff.Platform.Windows.csproj")]
    [InlineData("src/Griff.Scripting/Griff.Scripting.csproj")]
    [InlineData("src/Griff.Exporting/Griff.Exporting.csproj")]
    [InlineData("src/Griff.Graphics/Griff.Graphics.csproj")]
    [InlineData("src/Griff.Graphics.OpenGL/Griff.Graphics.OpenGL.csproj")]
    [InlineData("src/Griff.Fonts/Griff.Fonts.csproj")]
    [InlineData("src/Griff.Editing/Griff.Editing.csproj")]
    [InlineData("src/Griff.Agent/Griff.Agent.csproj")]
    public void Host_BuildsOnNoModule(string project) =>
        Assert.DoesNotContain(ProjectClosure(project), p => p.StartsWith("src/Modules/", StringComparison.OrdinalIgnoreCase));

    // A game carries none of the editor's parts: the players build on neither Griff.Editing nor Griff.Agent.
    [Theory]
    [InlineData("src/Griff.Web/Griff.Web.csproj")]
    [InlineData("src/Griff.Player/Griff.Player.csproj")]
    public void ThePlayers_CarryNothingOfTheEditors(string project) =>
        Assert.DoesNotContain(ProjectClosure(project), p => p.Contains("Griff.Editing", StringComparison.OrdinalIgnoreCase) ||
                                                            p.Contains("Griff.Agent", StringComparison.OrdinalIgnoreCase) ||
                                                            p.Contains("Griff.Editor", StringComparison.OrdinalIgnoreCase));

    // The same, as the compiler built them: the libraries reference no module's assembly.
    [Theory]
    [InlineData(typeof(Griff.Scripting.ScriptCompiler))]
    [InlineData(typeof(Griff.Exporting.GameExporter))]
    [InlineData(typeof(EditorModules))]
    [InlineData(typeof(Griff.Agent.McpServer))]
    [InlineData(typeof(Griff.Fonts.FontCatalog))]
    public void ALibrary_ReferencesNoModule(Type type) =>
        Assert.DoesNotContain(GriffsReferences(type.Assembly), r => r.StartsWith(ModulesNamespace + ".", StringComparison.Ordinal));

    [Fact]
    public void EachModule_LivesInItsOwnFolderAndNamespace_WithOneModuleClass()
    {
        var (compilation, trees) = LayerTests.Core();
        var folders = LayerTests.ModuleFolders().Select(Path.GetFileName).ToList();
        var others = LayerTests.OtherProjects().ToList();
        var problems = new List<string>();

        var moduleTrees = trees.Where(t => LayerTests.IsModuleFile(t.FilePath))
            .Concat(others.Where(p => p.Name.StartsWith("Modules/", StringComparison.Ordinal)).SelectMany(p => p.Trees));
        foreach (var tree in moduleTrees)
        {
            var folder = LayerTests.ModuleOf(tree.FilePath);
            var expected = $"{ModulesNamespace}.{folder}";
            foreach (var ns in tree.GetRoot().DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            {
                if (ns.Name.ToString() != expected)
                    problems.Add($"{folder}/{Path.GetFileName(tree.FilePath)} is in namespace {ns.Name}, not {expected}.");
            }
        }

        // Nothing outside the folders claims a module's namespace.
        foreach (var tree in trees.Where(t => t.FilePath.Length > 0 && !LayerTests.IsModuleFile(t.FilePath))
                     .Concat(others.Where(p => !p.Name.StartsWith("Modules/", StringComparison.Ordinal)).SelectMany(p => p.Trees)))
        {
            foreach (var ns in tree.GetRoot().DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Where(n => n.Name.ToString().StartsWith(ModulesNamespace, StringComparison.Ordinal)))
                problems.Add($"{Path.GetFileName(tree.FilePath)} is in {ns.Name} but outside src/Modules.");
        }

        // Each folder: its two projects, named after it, and one module class, registered once (found, not listed).
        var gameModule = compilation.GetTypeByMetadataName("Griff.Runtime.GameModule")!;
        foreach (var folder in folders)
        {
            var at = Path.Combine(LayerTests.ModulesFolder(), folder!);
            if (!File.Exists(Path.Combine(at, $"{ModulesNamespace}.{folder}.csproj")))
                problems.Add($"Modules/{folder} has no {ModulesNamespace}.{folder}.csproj.");
            if (!File.Exists(Path.Combine(at, "Editor", $"{ModulesNamespace}.{folder}.Editor.csproj")))
                problems.Add($"Modules/{folder}/Editor has no {ModulesNamespace}.{folder}.Editor.csproj.");
            var modules = trees.Where(t => LayerTests.IsModuleFile(t.FilePath) && LayerTests.ModuleOf(t.FilePath) == folder)
                .SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                    .Select(c => compilation.GetSemanticModel(t).GetDeclaredSymbol(c) as INamedTypeSymbol))
                .Where(c => c?.BaseType is { } b && SymbolEqualityComparer.Default.Equals(b, gameModule))
                .ToList();
            if (modules.Count != 1)
                problems.Add($"Modules/{folder} has {modules.Count} module classes; it needs exactly one.");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Equal(folders.Select(f => $"{ModulesNamespace}.{f}").Order(), GameModule.All.Select(m => m.Namespace).Order());
        Assert.Equal(folders.Select(f => $"{ModulesNamespace}.{f}").Order(), ModuleAssemblies.All.Select(a => a.GetName().Name).Order());
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

    // The Griff assemblies an assembly is compiled against (Griff, Griff.Editing, a module...).
    private static List<string> GriffsReferences(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Griff", StringComparison.Ordinal))];

    // The projects a project builds on, itself included, as paths from the repository's root.
    private static List<string> ProjectClosure(string project)
    {
        var root = LayerTests.RepoRoot();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>([Path.GetFullPath(Path.Combine(root, project))]);
        while (pending.TryPop(out var path))
        {
            if (!seen.Add(path))
                continue;
            foreach (var reference in XDocument.Load(path).Descendants("ProjectReference"))
            {
                // The editor's players are built beside it without being referenced; they don't count here.
                if ((string?)reference.Attribute("ReferenceOutputAssembly") == "false")
                    continue;
                pending.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, ((string)reference.Attribute("Include")!).Replace('\\', Path.DirectorySeparatorChar))));
            }
        }

        return [.. seen.Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))];
    }
}
