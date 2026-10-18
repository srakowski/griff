using System.Globalization;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;

namespace Griff.Editor.Features.Play;

/// <summary>
/// Sets the version of the game's saved games (Game.SaveVersion, 0 to begin with), for the whole project: saved in
/// project.griff and used by the game played here and exported, from the next Play. Each saved game keeps the version it
/// was saved by, so raise it when an update changes the game's scripts or scenes in a way an older save needs bringing up
/// to date, and add a system that does it (ISaveUpgrader). Saves load by name whatever their version, so a property added
/// or removed needs nothing.
/// </summary>
public sealed class SaveVersionCommand(IProjectSession projectSession, IDialogService dialogs, IStatusService status) : EditorCommand
{
    public override string Id => "Game.SaveVersion";
    public override string Text => "Save _Version";
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 2, Order: 3);

    protected override bool CanExecuteCore() => projectSession.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (projectSession.Project is not { } project)
            return Task.CompletedTask;
        var answer = dialogs.Prompt("Save Version", "Version of the game's saves", project.SaveVersion.ToString(CultureInfo.InvariantCulture),
            text => Parse(text) is null ? "Give a whole number, 0 or more." : null);
        if (answer is null || Parse(answer) is not { } version || version == project.SaveVersion)
            return Task.CompletedTask;
        project.SetSaveVersion(version);
        status.Show($"Games are saved as version {version} (from the next Play)");
        return Task.CompletedTask;
    }

    private static int? Parse(string text) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) && version >= 0 ? version : null;
}
