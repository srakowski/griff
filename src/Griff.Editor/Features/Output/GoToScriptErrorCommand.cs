using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Scripts;
using Griff.Editor.Themes;

namespace Griff.Editor.Features.Output;

/// <summary>
/// Opens a script in the script editor at a problem Output reported, e.g. "Ship.cs(12,5)" for "Ship.cs(12,5): ; expected",
/// as double-clicking it in Output does. Without a pick, the newest one.
/// </summary>
public sealed partial class GoToScriptErrorCommand(IWorkspace workspace, OutputLog log, ScriptEditors scripts) : EditorCommand
{
    public override string Id => "Output.GoToScriptError";
    public override string Text => "Go to Script Error";
    public override string? Icon => Icons.Code;
    public override IReadOnlyList<string>? Choices => Places().Distinct().ToList();

    protected override bool CanExecuteCore() => workspace.Project is not null && Places().Any();

    protected override Task ExecuteCoreAsync() => Places().LastOrDefault() is { } newest ? ChooseCoreAsync(newest) : Task.CompletedTask;

    /// <summary>Opens the script at <paramref name="choice"/>: "Ship.cs(12,5)", or a whole Output line starting so.</summary>
    protected override Task ChooseCoreAsync(string choice)
    {
        if (ScriptLine().Match(choice.Trim()) is not { Success: true } match)
            throw new ArgumentException($"\"{choice}\" doesn't say where in a script, like \"Ship.cs(12,5)\".", nameof(choice));
        if (workspace.Project is not { } project || !Directory.Exists(project.AssetsPath))
            return Task.CompletedTask;
        var file = Directory.EnumerateFiles(project.AssetsPath, match.Groups["file"].Value, SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new ArgumentException($"There's no script {match.Groups["file"].Value} in Assets.", nameof(choice));
        scripts.Open(file, int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["column"].Value, CultureInfo.InvariantCulture));
        return Task.CompletedTask;
    }

    /// <summary>Whether an Output line says where in a script it is.</summary>
    public static bool IsScriptLine(string message) => ScriptLine().IsMatch(message);

    // Where each of Output's lines that names a place in a script points, oldest first.
    private IEnumerable<string> Places() =>
        log.Entries.Select(e => ScriptLine().Match(e.Message)).Where(m => m.Success).Select(m => m.Value);

    [GeneratedRegex(@"^(?<file>[^\/:*?""<>|()]+\.cs)\((?<line>\d+),(?<column>\d+)\)")]
    private static partial Regex ScriptLine();
}
