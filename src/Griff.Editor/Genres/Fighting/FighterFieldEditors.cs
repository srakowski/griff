using CommunityToolkit.Mvvm.Input;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Inspector;
using Griff.Modules.Fighting;

namespace Griff.Editor.Genres.Fighting;

/// <summary>
/// A fighter's moves (<see cref="MoveListAttribute"/>): each listed with its input drawn as arrows and its frames, and a button
/// opening the Move List (Entity > Edit Moves) to edit them.
/// </summary>
public sealed class MovesEditor(PropertyField field, MoveListAttribute attribute) : FieldEditor<MoveListAttribute>(field, attribute)
{
    /// <summary>The moves take the section's whole width, under the field's name.</summary>
    public override bool Stacks => true;

    /// <summary>The moves, a line each: "Fireball  ↓↘→ P  12/4/30", or the line as typed when it doesn't read.</summary>
    public IReadOnlyList<MoveLine> Moves => [.. Lines.Where(l => l.Trim().Length > 0).Select(l =>
        FighterMove.TryParse(l, out var move, out var problem)
            ? new MoveLine(move.Name, move.Input.Arrows(), move.Startup + move.Active + move.Recovery > 0 ? $"{move.Startup}/{move.Active}/{move.Recovery}" : "", null)
            : new MoveLine(l.Trim(), "", "", problem))];

    /// <summary>"3 moves", or that it has none yet.</summary>
    public string Summary => Moves.Count switch
    {
        0 => "No moves yet: the Move List adds them.",
        1 => "1 move",
        var n => $"{n} moves",
    };

    /// <summary>Opens the Move List on the selected fighter, to edit its moves.</summary>
    public void OpenMoveList() => Host.RunCommand<OpenMoveListCommand>();

    public IRelayCommand OpenCommand => _open ??= new RelayCommand(OpenMoveList);

    private IRelayCommand? _open;
}

/// <summary>A move as a <see cref="MovesEditor"/> lists it.</summary>
public sealed record MoveLine(string Name, string Input, string Frames, string? Problem)
{
    public bool HasProblem => Problem is not null;
}

/// <summary>
/// A fighter's hitboxes and hurtboxes (<see cref="FrameBoxesAttribute"/>): how many of each are on each clip, and a button
/// opening the Clip Editor (Art > Animation Clips) to draw them over the frames.
/// </summary>
public sealed class FrameBoxesEditor(PropertyField field, FrameBoxesAttribute attribute) : FieldEditor<FrameBoxesAttribute>(field, attribute)
{
    /// <summary>The boxes take the section's whole width, under the field's name.</summary>
    public override bool Stacks => true;

    /// <summary>Each clip with boxes: "Punch: 2 hitboxes, 3 hurtboxes over 4 frames".</summary>
    public IReadOnlyList<string> Clips => [.. FrameBox.ParseAll(Lines).GroupBy(b => b.Clip, StringComparer.OrdinalIgnoreCase).Select(g =>
        $"{(g.Key == FrameBox.OwnFrames ? "Every frame" : g.Key)}: {Count(g.Count(b => b.IsHit), "hitbox")}, {Count(g.Count(b => !b.IsHit), "hurtbox")} over {Count(g.Select(b => b.Frame).Distinct().Count(), "frame")}")];

    /// <summary>"7 boxes", or that it has none yet.</summary>
    public string Summary => Lines.Count(l => l.Trim().Length > 0) switch
    {
        0 => "No boxes yet: draw them over the frames in the Clip Editor.",
        1 => "1 box",
        var n => $"{n} boxes",
    };

    /// <summary>Opens the Clip Editor on the selected entity, to draw its boxes over its frames.</summary>
    public void OpenClipEditor() => Host.RunCommand<OpenClipEditorCommand>();

    public IRelayCommand OpenCommand => _open ??= new RelayCommand(OpenClipEditor);

    private IRelayCommand? _open;

    private static string Count(int n, string what) => $"{n} {what}{(n == 1 ? "" : what.EndsWith('x') ? "es" : "s")}";
}
