using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Griff.Exporting;
using Griff.Scripting;

namespace Griff.Tests.Architecture;

/// <summary>
/// Scripting is split by job: compiling a project's scripts (<c>Griff.Scripting</c>, Roslyn's compiler and nothing more)
/// is all a game played from its sources and the exporter need; exporting (<c>Griff.Exporting</c>: the desktop and web
/// exporters, the web server, third-party notices, export fonts) sits on the compiler; and the editor's language services
/// (Roslyn Workspaces and Features) and its debugger's instrumenter are the editor's. So the players, the compiler, the
/// exporter and the tools that only export pull in no Roslyn Workspaces, and the exporter names a game's program as its
/// platform does.
/// </summary>
public sealed partial class AssemblySplitTests
{
    // What the editor alone may use: Roslyn's language services and what they bring.
    private static readonly string[] LanguageServices =
        ["Microsoft.CodeAnalysis.Workspaces.Common", "Microsoft.CodeAnalysis.CSharp.Workspaces", "Microsoft.CodeAnalysis.Features", "Microsoft.CodeAnalysis.CSharp.Features"];

    // The compiler and the exporter, which build on no editor.
    private static readonly string[] NotEditor = ["src/Griff.Scripting", "src/Griff.Exporting"];

    [Theory]
    [InlineData("src/Griff.Scripting/Griff.Scripting.csproj")]
    [InlineData("src/Griff.Exporting/Griff.Exporting.csproj")]
    [InlineData("src/Griff.Player/Griff.Player.csproj")]
    [InlineData("src/Griff.Web/Griff.Web.csproj")]
    public void Only_the_editor_pulls_in_Roslyns_language_services(string project)
    {
        var packages = ProjectClosure(project).SelectMany(PackagesOf).ToList();
        var found = packages.Where(p => p.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase) &&
                                        (p.Contains("Workspaces", StringComparison.OrdinalIgnoreCase) || p.Contains("Features", StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.True(found.Count == 0, $"{project} pulls in the editor's language services: {string.Join(", ", found)}");

        // What restore resolved, packages' own dependencies included, when it has run.
        var assets = Path.Combine(LayerTests.RepoRoot(), Path.GetDirectoryName(project)!, "obj", "project.assets.json");
        if (File.Exists(assets))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(assets));
            var libraries = json.RootElement.GetProperty("libraries").EnumerateObject().Select(l => l.Name.Split('/')[0]).ToList();
            var resolved = libraries.Where(l => LanguageServices.Contains(l, StringComparer.OrdinalIgnoreCase)).ToList();
            Assert.True(resolved.Count == 0, $"{project} resolves the editor's language services: {string.Join(", ", resolved)}");
        }
    }

    [Theory]
    [InlineData("src/Griff.Player/Griff.Player.csproj")]
    [InlineData("src/Griff.Web/Griff.Web.csproj")]
    public void The_players_carry_no_compiler(string project)
    {
        var closure = ProjectClosure(project).ToList();
        Assert.DoesNotContain(closure, p => p.EndsWith("Griff.Scripting.csproj", StringComparison.OrdinalIgnoreCase) || p.EndsWith("Griff.Exporting.csproj", StringComparison.OrdinalIgnoreCase));
        var roslyn = closure.SelectMany(PackagesOf).Where(p => p.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(roslyn.Count == 0, $"{project} carries Roslyn: {string.Join(", ", roslyn)}");
    }

    [Fact]
    public void The_compiler_and_the_exporter_reference_no_language_services()
    {
        var referenced = References(typeof(GameExporter).Assembly).Concat(References(typeof(ScriptCompiler).Assembly)).Distinct().ToList();
        Assert.Contains("Microsoft.CodeAnalysis.CSharp", referenced);
        var found = referenced.Where(r => r.Contains("Workspaces", StringComparison.OrdinalIgnoreCase) || r.Contains("Features", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(found.Count == 0, "Compiling and exporting reference the editor's language services: " + string.Join(", ", found));
    }

    [Fact]
    public void The_debuggers_instrumenter_and_the_language_services_are_the_editors()
    {
        var root = LayerTests.RepoRoot();
        var found = NotEditor
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar, '/').Any(part => part is "bin" or "obj"))
            .SelectMany(path => EditorOnly().Matches(File.ReadAllText(path)).Select(m => $"{Path.GetRelativePath(root, path)}: {m.Value}"))
            .ToList();
        Assert.True(found.Count == 0, "The instrumenter, its probes and the language services live in the editor (Features/Debugging/Probes, Features/Scripts/Language):\n" + string.Join("\n", found));
    }

    [Fact]
    public void The_exporter_asks_the_platform_for_a_programs_name()
    {
        var root = LayerTests.RepoRoot();
        var found = Directory.EnumerateFiles(Path.Combine(root, "src", "Griff.Exporting"), "*.cs", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "DesktopPlatform.cs" && !path.Split(Path.DirectorySeparatorChar, '/').Any(part => part is "bin" or "obj"))
            .SelectMany(path => ExeLiteral().Matches(File.ReadAllText(path)).Select(m => $"{Path.GetRelativePath(root, path)}: {m.Value}"))
            .ToList();
        Assert.True(found.Count == 0, "A program's name is its platform's (DesktopPlatform.ExecutableName):\n" + string.Join("\n", found));
    }

    // The projects a project builds on, itself included, as paths from the repository's root.
    private static IEnumerable<string> ProjectClosure(string project)
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

        return seen.Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'));
    }

    private static IEnumerable<string> PackagesOf(string project) =>
        XDocument.Load(Path.Combine(LayerTests.RepoRoot(), project)).Descendants("PackageReference").Select(p => (string)p.Attribute("Include")!);

    // The names of the assemblies an assembly is compiled against, theirs included, but .NET's own.
    private static HashSet<string> References(Assembly assembly)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<AssemblyName>(assembly.GetReferencedAssemblies());
        while (pending.TryPop(out var name))
        {
            if (name.Name is not { } simple || simple.StartsWith("System", StringComparison.Ordinal) || simple is "mscorlib" or "netstandard" || !seen.Add(simple))
                continue;
            foreach (var next in Assembly.Load(name).GetReferencedAssemblies())
                pending.Push(next);
        }

        return seen;
    }

    [GeneratedRegex(@"\b(ScriptInstrumenter|ProbeTable|ScriptLanguage|AdhocWorkspace|Microsoft\.CodeAnalysis\.(Completion|Classification|QuickInfo|FindSymbols|Rename|Host\.Mef))\b")]
    private static partial Regex EditorOnly();

    [GeneratedRegex(@"""\.exe""")]
    private static partial Regex ExeLiteral();
}
