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

public sealed class CompileResult
{
    public required bool Success { get; init; }

    public required IReadOnlyList<ScriptDiagnostic> Diagnostics { get; init; }

    public byte[]? Assembly { get; init; }

    public byte[]? Symbols { get; init; }

    /// <summary>The scripts' <c>///</c> comments as documentation XML, for the editor's component catalog.</summary>
    public string? Documentation { get; init; }

    /// <summary>
    /// The breakpoint probes in a build made with them (see <see cref="ScriptInstrumenter"/>), to be attached to the loaded
    /// assembly; empty otherwise.
    /// </summary>
    public ProbeTable Probes { get; init; } = ProbeTable.Empty;

    public IEnumerable<ScriptDiagnostic> Errors => Diagnostics.Where(d => d.IsError);
}

/// <summary>
/// Compiles a project's C# scripts in memory with Roslyn. Scripts get implicit usings for the common
/// Griff and System namespaces, so a new script is just a class.
/// </summary>
public static class ScriptCompiler
{
    public const string ImplicitUsingsPath = "<griff implicit usings>";

    /// <summary>
    /// The usings every script gets without writing them: System's common namespaces, Griff's, and each module's (see
    /// <see cref="GameModule"/>), so a script names a Match Board or a Unit as it does a Sprite.
    /// </summary>
    public static string ImplicitUsings { get; } = """
        global using System;
        global using System.Collections;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Numerics;
        global using Griff;
        global using Griff.Input;
        global using Griff.Runtime;
        global using Griff.Scenes;

        """ + string.Concat(GameModule.All.Select(m => $"global using {m.Namespace};{Environment.NewLine}"));

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

    public static CSharpParseOptions ParseOptions { get; } = new(LanguageVersion.Latest);

    /// <summary>What scripts compile against: the runtime's System assemblies and Griff.</summary>
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

    /// <summary>Reads and compiles every *.cs file under <paramref name="folder"/>.</summary>
    /// <param name="optimize">Compile for an exported game (<see cref="ExportCompilationOptions"/>) rather than the editor.</param>
    public static CompileResult CompileFolder(string folder, bool optimize = false) => Compile(ReadFolder(folder), optimize);

    /// <summary>Every *.cs file under <paramref name="folder"/>, in a stable order.</summary>
    public static IReadOnlyList<ScriptSource> ReadFolder(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(path => new ScriptSource(path, ReadShared(path)))
                .ToList()
            : [];

    /// <param name="probes">
    /// Put breakpoint probes in (see <see cref="ScriptInstrumenter"/>), as the editor does: each line then tests a switch
    /// of its own, which a breakpoint turns on.
    /// </param>
    public static CompileResult Compile(IReadOnlyList<ScriptSource> sources, bool optimize = false, bool probes = false)
    {
        var trees = sources
            .Select(s => CSharpSyntaxTree.ParseText(s.Text, ParseOptions, s.Path, System.Text.Encoding.UTF8))
            .Append(CSharpSyntaxTree.ParseText(ImplicitUsings, ParseOptions, ImplicitUsingsPath, System.Text.Encoding.UTF8))
            .ToList();

        var compilation = CSharpCompilation.Create(
            $"GriffScripts_{Guid.NewGuid():N}",
            trees,
            References.Value,
            optimize ? ExportCompilationOptions : CompilationOptions);

        if (probes)
        {
            var result = CompileWithProbes(compilation);
            if (result.Probes != ProbeTable.Empty)
                result.Probes.KeepSources(sources);
            return result;
        }

        var emitted = Emit(compilation);
        var diagnostics = Report(emitted.Result.Diagnostics);
        return new CompileResult
        {
            Success = emitted.Result.Success,
            Diagnostics = diagnostics,
            Assembly = emitted.Result.Success ? emitted.Assembly : null,
            Symbols = emitted.Result.Success ? emitted.Symbols : null,
            Documentation = emitted.Result.Success ? emitted.Documentation : null,
        };
    }

    // The scripts' own errors and warnings come from them as written; the build has the probes in. A probe that doesn't
    // compile (something the analysis didn't foresee) is left out, so a script that compiles always builds.
    private static CompileResult CompileWithProbes(CSharpCompilation compilation)
    {
        var diagnostics = Report(compilation.GetDiagnostics());
        if (diagnostics.Any(d => d.IsError))
            return new CompileResult { Success = false, Diagnostics = diagnostics };

        var plan = ScriptInstrumenter.Analyze(compilation, ImplicitUsingsPath);
        var leftOut = new HashSet<int>();
        var refined = new HashSet<int>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var (instrumented, table) = plan.Build(leftOut);
            var emitted = Emit(instrumented);
            if (emitted.Result.Success)
                return Built(emitted, diagnostics, table);

            // A probe that doesn't compile is worked out again with Roslyn's help, and if it still doesn't, left out.
            var failing = new HashSet<int>();
            foreach (var error in emitted.Result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                var span = error.Location.GetLineSpan();
                failing.UnionWith(plan.Around(span.Path, span.StartLinePosition.Line + 1));
            }

            var unrefined = failing.Except(refined).ToList();
            if (unrefined.Count > 0)
            {
                plan.Refine(unrefined);
                refined.UnionWith(unrefined);
                continue;
            }

            var before = leftOut.Count;
            leftOut.UnionWith(failing);
            if (leftOut.Count == before)
                break;
        }

        var plain = Emit(compilation);
        return plain.Result.Success
            ? Built(plain, diagnostics, new ProbeTable([]))
            : new CompileResult { Success = false, Diagnostics = Report(plain.Result.Diagnostics) };
    }

    private static CompileResult Built((EmitResult Result, byte[] Assembly, byte[] Symbols, string Documentation) emitted, IReadOnlyList<ScriptDiagnostic> diagnostics, ProbeTable probes) => new()
    {
        Success = true,
        Diagnostics = diagnostics,
        Assembly = emitted.Assembly,
        Symbols = emitted.Symbols,
        Documentation = emitted.Documentation,
        Probes = probes,
    };

    private static (EmitResult Result, byte[] Assembly, byte[] Symbols, string Documentation) Emit(CSharpCompilation compilation)
    {
        using var assembly = new MemoryStream();
        using var symbols = new MemoryStream();
        using var documentation = new MemoryStream();
        var result = compilation.Emit(assembly, symbols, documentation, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        return (result, assembly.ToArray(), symbols.ToArray(), System.Text.Encoding.UTF8.GetString(documentation.ToArray()));
    }

    private static List<ScriptDiagnostic> Report(IEnumerable<Diagnostic> diagnostics) => diagnostics
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

    // The editor's own runtime assemblies (the "trusted platform assemblies") plus Griff itself. Each carries its XML
    // documentation when there is some, for the script editor's tooltips; compiling ignores it.
    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(p) is "mscorlib.dll" or "netstandard.dll")
            .Append(typeof(Component).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return [.. paths.Select(p => MetadataReference.CreateFromFile(p, documentation: ScriptDocumentation.For(p)))];
    }

    // Editors may hold the file open while saving; read without locking it.
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
