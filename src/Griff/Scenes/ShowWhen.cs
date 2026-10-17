namespace Griff.Scenes;

/// <summary>
/// Shows its entity only while a flag holds, with no code: a HUD whose tools appear one by one as goals are met, a sign
/// that's put up once the guard has been talked to. While <see cref="NeedsFlag"/> holds the entity is shown, and otherwise
/// it's hidden with everything inside it, looked at again every frame: it appears as soon as a trigger's Set Flag, a
/// dialogue's "[set Name]" or a script's <c>Game.SetFlag</c> sets the flag, and goes again when the flag is cleared or a
/// New Game forgets it. A hidden button can't be clicked and its shortcut does nothing.
/// </summary>
/// <remarks>
/// It decides whether its own entity is shown, so to hide it some other way, hide what it's inside. Name a flag starting
/// "run." ("run.Contracts") for one that lasts only while the game runs: it isn't kept for the next time the game starts,
/// so an unlock doesn't carry over (see <c>Game.SetFlag</c>). For a panel, use its own Needs Flag, which says when it can
/// open. The Scene view shows the entity as ever; it's only hidden while the game runs.
/// </remarks>
[Category("Gameplay")]
[ComponentName("Griff.ShowWhen")]
public sealed class ShowWhen : Component
{
    /// <summary>
    /// The flag that has to be set for the entity to show, e.g. "run.Contracts" (set by a trigger's Set Flag, a dialogue's
    /// "[set run.Contracts]" or a script's <c>Game.SetFlag</c>), or "not Tutorial" to show it only while that isn't set.
    /// Empty leaves the entity as it is.
    /// </summary>
    [Names(NameKind.Flag, Condition = true)]
    public string? NeedsFlag { get; set; }

    public override string? SetupNote() =>
        string.IsNullOrWhiteSpace(NeedsFlag) ? null : $"Shown only while {Condition(NeedsFlag.Trim())}.";

    // "run.Contracts" reads "run.Contracts is set"; "not Tutorial", "Tutorial isn't set".
    private static string Condition(string needs)
    {
        if (needs.StartsWith('!'))
            return $"{needs[1..].TrimStart()} isn't set";
        return needs.StartsWith("not ", StringComparison.OrdinalIgnoreCase) && needs.Length > 4 ? $"{needs[4..].TrimStart()} isn't set" : $"{needs} is set";
    }
}
