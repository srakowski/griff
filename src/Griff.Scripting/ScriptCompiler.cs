using System.Collections.Immutable;
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

    public IEnumerable<ScriptDiagnostic> Errors => Diagnostics.Where(d => d.IsError);
}

/// <summary>
/// Compiles a project's C# scripts in memory with Roslyn. Scripts get implicit usings for the common
/// Griff and System namespaces, so a new script is just a class.
/// </summary>
public static class ScriptCompiler
{
    public const string ImplicitUsingsPath = "<griff implicit usings>";

    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Numerics;
        global using Griff;
        global using Griff.Runtime;
        global using Griff.Scenes;
        """;

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    /// <summary>Reads and compiles every *.cs file under <paramref name="folder"/>.</summary>
    public static CompileResult CompileFolder(string folder)
    {
        var sources = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(path => new ScriptSource(path, ReadShared(path)))
                .ToList()
            : [];
        return Compile(sources);
    }

    public static CompileResult Compile(IReadOnlyList<ScriptSource> sources)
    {
        var trees = sources
            .Select(s => CSharpSyntaxTree.ParseText(s.Text, ParseOptions, s.Path, System.Text.Encoding.UTF8))
            .Append(CSharpSyntaxTree.ParseText(ImplicitUsings, ParseOptions, ImplicitUsingsPath, System.Text.Encoding.UTF8))
            .ToList();

        var compilation = CSharpCompilation.Create(
            $"GriffScripts_{Guid.NewGuid():N}",
            trees,
            References.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                optimizationLevel: OptimizationLevel.Debug));

        using var assembly = new MemoryStream();
        using var symbols = new MemoryStream();
        var result = compilation.Emit(assembly, symbols, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

        var diagnostics = result.Diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning && !d.IsSuppressed)
            .Select(ToScriptDiagnostic)
            .OrderBy(d => !d.IsError)
            .ToList();

        return new CompileResult
        {
            Success = result.Success,
            Diagnostics = diagnostics,
            Assembly = result.Success ? assembly.ToArray() : null,
            Symbols = result.Success ? symbols.ToArray() : null,
        };
    }

    private static ScriptDiagnostic ToScriptDiagnostic(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        return new ScriptDiagnostic(
            span.Path,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            diagnostic.GetMessage(),
            diagnostic.Severity == DiagnosticSeverity.Error);
    }

    // The editor's own runtime assemblies (the "trusted platform assemblies") plus Griff itself.
    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(p) is "mscorlib.dll" or "netstandard.dll")
            .Append(typeof(Component).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return [.. paths.Select(p => MetadataReference.CreateFromFile(p))];
    }

    // Editors may hold the file open while saving; read without locking it.
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
