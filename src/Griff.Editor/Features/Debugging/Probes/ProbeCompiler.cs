using Griff.Scripting;
using Microsoft.CodeAnalysis;

namespace Griff.Editor.Features.Debugging.Probes;

/// <summary>
/// Builds the scripts as the editor plays them: with breakpoint probes (see <see cref="ScriptInstrumenter"/>), so a
/// breakpoint can stop them without building them again. The build's <see cref="ProbeTable"/> is its
/// <see cref="CompileResult.Attachment"/>, which <see cref="ScriptHost"/> attaches to the loaded assembly.
/// </summary>
public static class ProbeCompiler
{
    /// <summary>
    /// Compiles <paramref name="sources"/> with a probe on each line of code. The scripts' own errors and warnings come from
    /// them as written; the build has the probes in. A probe that doesn't compile (something the analysis didn't foresee) is
    /// left out, so a script that compiles always builds.
    /// </summary>
    public static CompileResult Compile(IReadOnlyList<ScriptSource> sources)
    {
        var result = Build(ScriptCompiler.CreateCompilation(sources));
        if (result.Attachment is ProbeTable table)
            table.KeepSources(sources);
        return result;
    }

    private static CompileResult Build(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation)
    {
        var diagnostics = ScriptCompiler.Report(compilation.GetDiagnostics());
        if (diagnostics.Any(d => d.IsError))
            return new CompileResult { Success = false, Diagnostics = diagnostics };

        var plan = ScriptInstrumenter.Analyze(compilation, ScriptCompiler.ImplicitUsingsPath);
        var leftOut = new HashSet<int>();
        var refined = new HashSet<int>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var (instrumented, table) = plan.Build(leftOut);
            var emitted = ScriptCompiler.Emit(instrumented);
            if (emitted.Success)
                return emitted with { Diagnostics = diagnostics, Attachment = table };

            // A probe that doesn't compile is worked out again with Roslyn's help, and if it still doesn't, left out.
            var failing = new HashSet<int>();
            foreach (var error in emitted.Errors)
                failing.UnionWith(plan.Around(error.File, error.Line));

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

        var plain = ScriptCompiler.Emit(compilation);
        return plain.Success ? plain with { Diagnostics = diagnostics, Attachment = new ProbeTable([]) } : plain;
    }

    extension(CompileResult result)
    {
        /// <summary>The breakpoint probes of a build made with them; empty for one without.</summary>
        public ProbeTable Probes => result.Attachment as ProbeTable ?? ProbeTable.Empty;
    }
}
