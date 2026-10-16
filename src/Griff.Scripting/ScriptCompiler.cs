using System.Collections.Immutable;
using Griff.Runtime;
using Griff.Scenes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Griff.Scripting;

/// <summary>A compiler message tied to a script file and line, e.g. for "Player.cs(12): ; expected".</summary>
public sealed record ScriptDiagnostic(string File, int Line, int Column, string Message, bool IsError)
{
    public override string ToString() => $"{Path.GetFileName(File)}({Line},{Column}): {Message}";
}

public sealed record ScriptSource(string Path, string Text);

public sealed record CompileResult
{
    public required bool Success { get; init; }

    public required IReadOnlyList<ScriptDiagnostic> Diagnostics { get; init; }

    public byte[]? Assembly { get; init; }

    public byte[]? Symbols { get; init; }

    /// <summary>The scripts' <c>///</c> comments as documentation XML, for the editor's component catalog.</summary>
    public string? Documentation { get; init; }

    /// <summary>
    /// What a build made with more than the scripts' code carries, tied to its assembly once loaded (the editor's breakpoint
    /// probes); null for a plain build.
    /// </summary>
    public IScriptAttachment? Attachment { get; init; }

    public IEnumerable<ScriptDiagnostic> Errors => Diagnostics.Where(d => d.IsError);
}

/// <summary>
/// What a build of the scripts carries besides their code, tied to the assembly they load as: the editor's debugger builds
/// them with breakpoint probes, whose switches it finds in the loaded assembly. <see cref="ScriptHost"/> attaches it when it
/// loads the build and detaches it when the build is replaced or unloaded.
/// </summary>
public interface IScriptAttachment
{
    /// <summary>The build's assembly has loaded.</summary>
    void Attach(System.Reflection.Assembly assembly);

    /// <summary>The build is replaced or unloaded; its code may still be running (held at a breakpoint), and stops using this.</summary>
    void Detach();
}

/// <summary>
/// Compiles a project's C# scripts in memory with Roslyn. Scripts get implicit usings for the common
/// Griff and System namespaces, so a new script is just a class.
/// </summary>
public static class ScriptCompiler
{
    public const string ImplicitUsingsPath = "<griff implicit usings>";

    /// <summary>
    /// The usings every script gets without writing them: System's common namespaces and Griff's, then those of the modules
    /// its project uses (<see cref="ImplicitUsingsFor"/>).
    /// </summary>
    public const string CommonUsings = """
        global using System;
        global using System.Collections;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Numerics;
        global using Griff;
        global using Griff.Data;
        global using Griff.Input;
        global using Griff.Runtime;
        global using Griff.Scenes;
        global using Griff.Simulations;

        """;

    /// <summary>
    /// The usings a project's scripts get without writing them: <see cref="CommonUsings"/>, and each module's the project uses
    /// (<paramref name="modules"/>, see <see cref="ProjectModules"/>), so a script names a Match Board or a Unit as it does a
    /// Sprite. A module no scene or prefab uses yet is a <c>using Griff.Modules.Match3;</c> away; the scripts compile against
    /// every module this program has.
    /// </summary>
    public static string ImplicitUsingsFor(IEnumerable<GameModule> modules) =>
        CommonUsings + string.Concat(modules.Select(m => m.Namespace).Distinct().Order(StringComparer.Ordinal)
            .Select(ns => $"global using {ns};{Environment.NewLine}"));

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

    public static CSharpParseOptions ParseOptions { get; } = new(LanguageVersion.Latest);

    /// <summary>What scripts compile against: the runtime's System assemblies, Griff and its modules.</summary>
    public static ImmutableArray<MetadataReference> MetadataReferences => References.Value;

    public static CSharpCompilationOptions CompilationOptions { get; } = new(
        OutputKind.DynamicallyLinkedLibrary,
        nullableContextOptions: NullableContextOptions.Enable,
        optimizationLevel: OptimizationLevel.Debug,
        // Documentation is optional in scripts; don't nag about public members without it.
        specificDiagnosticOptions: [new("CS1591", ReportDiagnostic.Suppress)]);

    /// <summary>
    /// For exported games: optimized, like a Release build. The editor compiles with <see cref="CompilationOptions"/>
    /// (Debug) so hot-reloaded scripts can be stepped through.
    /// </summary>
    public static CSharpCompilationOptions ExportCompilationOptions { get; } =
        CompilationOptions.WithOptimizationLevel(OptimizationLevel.Release);

    /// <summary>
    /// Reads and compiles every *.cs file under <paramref name="folder"/> (a project's Assets), with a <c>using</c> for each
    /// module its scenes and prefabs use (<see cref="ProjectModules.InFiles"/>).
    /// </summary>
    /// <param name="optimize">Compile for an exported game (<see cref="ExportCompilationOptions"/>) rather than the editor.</param>
    public static CompileResult CompileFolder(string folder, bool optimize = false) =>
        Compile(ReadFolder(folder), optimize, ProjectModules.InFiles(folder));

    /// <summary>Every *.cs file under <paramref name="folder"/>, in a stable order.</summary>
    public static IReadOnlyList<ScriptSource> ReadFolder(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(path => new ScriptSource(path, ReadShared(path)))
                .ToList()
            : [];

    /// <summary>Compiles <paramref name="sources"/>.</summary>
    /// <param name="optimize">Compile for an exported game (<see cref="ExportCompilationOptions"/>) rather than the editor.</param>
    /// <param name="modules">The modules their project uses, which they get a <c>using</c> for; null for every one (scripts with no project).</param>
    public static CompileResult Compile(IReadOnlyList<ScriptSource> sources, bool optimize = false, IEnumerable<GameModule>? modules = null) =>
        Emit(CreateCompilation(sources, optimize, modules));

    /// <summary>
    /// The compilation of <paramref name="sources"/> with the implicit usings, before it's emitted: what a build that adds
    /// to the scripts (the editor's breakpoint probes) starts from, and emits with <see cref="Emit"/>.
    /// </summary>
    /// <param name="modules">The modules their project uses, which they get a <c>using</c> for; null for every one (scripts with no project).</param>
    public static CSharpCompilation CreateCompilation(IReadOnlyList<ScriptSource> sources, bool optimize = false, IEnumerable<GameModule>? modules = null)
    {
        var usings = ImplicitUsingsFor(modules ?? GameModule.All);
        var trees = sources
            .Select(s => CSharpSyntaxTree.ParseText(s.Text, ParseOptions, s.Path, System.Text.Encoding.UTF8))
            .Append(CSharpSyntaxTree.ParseText(usings, ParseOptions, ImplicitUsingsPath, System.Text.Encoding.UTF8))
            .ToList();

        return CSharpCompilation.Create(
            $"GriffScripts_{Guid.NewGuid():N}",
            trees,
            References.Value,
            optimize ? ExportCompilationOptions : CompilationOptions);
    }

    /// <summary>Emits <paramref name="compilation"/>: its assembly, symbols and documentation, or its errors.</summary>
    public static CompileResult Emit(CSharpCompilation compilation)
    {
        using var assembly = new MemoryStream();
        using var symbols = new MemoryStream();
        using var documentation = new MemoryStream();
        var result = compilation.Emit(assembly, symbols, documentation, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        return new CompileResult
        {
            Success = result.Success,
            Diagnostics = Report(result.Diagnostics),
            Assembly = result.Success ? assembly.ToArray() : null,
            Symbols = result.Success ? symbols.ToArray() : null,
            Documentation = result.Success ? System.Text.Encoding.UTF8.GetString(documentation.ToArray()) : null,
        };
    }

    /// <summary>The errors and warnings of <paramref name="diagnostics"/> that aren't suppressed, errors first.</summary>
    public static IReadOnlyList<ScriptDiagnostic> Report(IEnumerable<Diagnostic> diagnostics) => diagnostics
        .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning && !d.IsSuppressed)
        .Select(ToScriptDiagnostic)
        .OrderBy(d => !d.IsError)
        .ToList();

    private static ScriptDiagnostic ToScriptDiagnostic(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        return new ScriptDiagnostic(
            span.Path,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            diagnostic.GetMessage(System.Globalization.CultureInfo.CurrentCulture),
            diagnostic.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// The files of the assemblies scripts compile against: the runtime's own System assemblies (the "trusted platform
    /// assemblies"), Griff, and the running parts of every module this program has (see <see cref="ModuleAssemblies"/>). The
    /// script editor reads them with their documentation, for its tooltips.
    /// </summary>
    public static IReadOnlyList<string> ReferencePaths { get; } = [.. ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(p) is "mscorlib.dll" or "netstandard.dll")
        .Append(typeof(Component).Assembly.Location)
        .Concat(ModuleAssemblies.All.Select(a => a.Location).Where(p => p.Length > 0))
        .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static ImmutableArray<MetadataReference> LoadReferences() =>
        [.. ReferencePaths.Select(p => MetadataReference.CreateFromFile(p))];

    // Editors may hold the file open while saving; read without locking it.
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
