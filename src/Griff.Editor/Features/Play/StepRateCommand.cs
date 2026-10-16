using System.Globalization;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Runtime;

namespace Griff.Editor.Features.Play;

/// <summary>
/// Sets how many steps a second the game takes (its step rate; 60 is usual), for the whole project: saved in
/// project.griff, used by the game played here and exported, and by a running game straight away. A faster rate gives
/// fast bodies finer steps at more work a second; the screen is drawn smoothly between steps whatever it is. Pick one of
/// the usual rates, or run it without a pick (or pick a number) for any from 10 to 240. It's undone like an edit.
/// Scripts can still change Time.StepRate as the game runs.
/// </summary>
public sealed class StepRateCommand(IProjectSession projectSession, ISceneDocuments sceneDocuments, IPlaySession playSession, PlayMode play, IDialogService dialogs, IStatusService status) : EditorCommand
{
    private static readonly int[] Usual = [30, 60, 120, 240];

    public override string Id => "Game.StepRate";
    public override string Text => "Step _Rate";
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 2, Order: 0);

    public override IReadOnlyList<string>? Choices =>
        projectSession.Project is { } project ? Usual.Append(project.StepRate).Distinct().Order().Select(Label).ToList() : [];

    public override string? Chosen => projectSession.Project is { } project ? Label(project.StepRate) : null;

    protected override bool CanExecuteCore() => projectSession.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (projectSession.Project is not { } project)
            return Task.CompletedTask;
        var answer = dialogs.Prompt("Step Rate", "Steps a second", project.StepRate.ToString(CultureInfo.InvariantCulture),
            text => Parse(text) is null ? $"Give a whole number from {Time.MinStepRate} to {Time.MaxStepRate}." : null);
        if (answer is not null && Parse(answer) is { } rate)
            Set(project, rate);
        return Task.CompletedTask;
    }

    protected override Task ChooseCoreAsync(string choice)
    {
        if (projectSession.Project is not { } project)
            return Task.CompletedTask;
        var rate = Parse(choice)
            ?? throw new ArgumentException($"\"{choice}\" isn't a step rate: pick one of {string.Join(", ", Choices ?? [])}, or a number from {Time.MinStepRate} to {Time.MaxStepRate}.", nameof(choice));
        Set(project, rate);
        return Task.CompletedTask;
    }

    private static string Label(int rate) => $"{rate} steps a second";

    // "120", or "120 steps a second" as the submenu says it.
    private static int? Parse(string text)
    {
        var number = text.Trim().Split(' ', 2)[0];
        return int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rate) && rate is >= Time.MinStepRate and <= Time.MaxStepRate
            ? rate
            : null;
    }

    // Recorded where Edit > Undo finds it: the scene or prefab showing, or while playing, the scene being edited (whose
    // history outlasts the run).
    private void Set(Griff.Projects.Project project, int rate)
    {
        if (rate == project.StepRate)
            return;
        var edit = new SetStepRateEdit(project, rate, now =>
        {
            play.UseStepRate(now);
            status.Show($"The game takes {now} steps a second" + (play.IsPlaying ? " (the running game too)" : ""));
        });
        if ((playSession.IsPlaying ? sceneDocuments.EditScene : sceneDocuments.Scene) is { } document)
            document.Execute(edit);
        else
            edit.Apply();
    }
}
