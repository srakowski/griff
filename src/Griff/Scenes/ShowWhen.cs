namespace Griff.Scenes;

/// <summary>
/// Shows its entity only while a flag holds, with no code: a HUD whose tools appear one by one as goals are met, a sign
/// that's put up once the guard has been talked to. While <see cref="NeedsFlag"/> holds the entity is shown, and otherwise
/// it's hidden with everything inside it, looked at again every frame: it appears as soon as a trigger's Set Flag, a
/// dialogue's "[set Name]" or a script's <c>Game.SetFlag</c> sets the flag, and goes again when the flag is cleared or a
/// New Game forgets it. It can also (or instead) show the entity only while the camera is zoomed in (<see cref="ZoomBelow"/>). A hidden button can't be clicked and its shortcut does nothing.
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
    /// Several joined with "or" show it while any holds: "run.Contracts or run.Work". Empty leaves the entity as it is.
    /// </summary>
    [Names(NameKind.Flag, Condition = true)]
    public string? NeedsFlag { get; set; }

    /// <summary>
    /// Shows the entity only while the main camera is zoomed in this far: while its Size is at most this (a smaller Size is
    /// closer in), and hides it when the camera pulls out past it. A label under each worker with 7 shows while the camera's
    /// Size is 7 or less, so a busy floor isn't drowned in words when seen from afar. 0 ignores the zoom. With a
    /// <see cref="NeedsFlag"/> too, both have to hold. Hidden as the flag hides it, with everything inside it.
    /// </summary>
    public float ZoomBelow { get; set; }

    public override string? SetupNote()
    {
        var flag = string.IsNullOrWhiteSpace(NeedsFlag) ? null : Condition(NeedsFlag.Trim());
        var zoom = ZoomBelow > 0 ? $"the camera's Size is at most {ZoomBelow:0.##}" : null;
        return flag is null && zoom is null ? null : $"Shown only while {string.Join(" and ", new[] { flag, zoom }.OfType<string>())}.";
    }

    // "run.Contracts" reads "run.Contracts is set"; "not Tutorial", "Tutorial isn't set"; "A or B", "A is set or B is set".
    private static string Condition(string needs) =>
        string.Join(" or ", System.Text.RegularExpressions.Regex.Split(needs, @"\s+or\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(p => One(p.Trim())));

    private static string One(string needs)
    {
        if (needs.StartsWith('!'))
            return $"{needs[1..].TrimStart()} isn't set";
        return needs.StartsWith("not ", StringComparison.OrdinalIgnoreCase) && needs.Length > 4 ? $"{needs[4..].TrimStart()} isn't set" : $"{needs} is set";
    }
}
