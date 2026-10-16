namespace Griff.Scenes;

/// <summary>
/// A component that puts its entity (and what's inside it) on a team: a <see cref="Scenes.Team"/>, a
/// <see cref="TurnTaker"/>, a strategy unit, building or faction, a fighter's hitboxes. Every genre's teams are the one
/// idea (see <see cref="Teams"/>), so a platformer hero on the team "Blue" is a friend of the blue tactics units, takes
/// turns as Blue and spends Blue's gold. A script component can implement it too.
/// </summary>
public interface ITeamMember
{
    /// <summary>The team's name, e.g. "Blue"; empty for none.</summary>
    string? Team { get; }
}

/// <summary>
/// Teams, the one idea every genre shares: a name ("Player", "Blue") that a <see cref="Scenes.Team"/>, a turn order's
/// <see cref="TurnOrder.Teams"/>, a strategy faction or a network game's <see cref="Multiplayer.Teams"/> gives, and that
/// anything on that team (see <see cref="ITeamMember"/>) carries. Names are the same team whatever their case and the
/// spaces at their ends. Things on the same team are allies (they don't hurt each other, take turns together and share
/// the team's items); things on different teams are foes; something on no team is neither.
/// </summary>
public static class Teams
{
    /// <summary>What a team's name is a kind of, for <see cref="NamesAttribute.Scope"/>: every genre's teams share it.</summary>
    public const string Scope = "Team";

    /// <summary>
    /// The team <paramref name="entity"/> is on: the first team named by a component on it (see <see cref="ITeamMember"/>),
    /// else on the nearest entity it's inside (a sword's hitbox is on its wielder's team). Empty for none. Allocates nothing.
    /// </summary>
    public static string Of(Entity? entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            var components = e.Components;
            for (var i = 0; i < components.Count; i++)
            {
                if (components[i] is ITeamMember member && !string.IsNullOrWhiteSpace(member.Team))
                    return member.Team;
            }
        }

        return "";
    }

    /// <summary>True if <paramref name="a"/> and <paramref name="b"/> name the same team (ignoring case and spaces at the ends; two empty names are the same).</summary>
    public static bool Same(string? a, string? b) =>
        a.AsSpan().Trim().Equals(b.AsSpan().Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>True if both are on a team, the same one.</summary>
    public static bool Allies(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && Same(a, b);

    /// <summary>True if both are on a team, different ones.</summary>
    public static bool Foes(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && !Same(a, b);

    /// <summary>True if <paramref name="a"/> and <paramref name="b"/> are on the same team (see <see cref="Of"/>).</summary>
    public static bool Allies(Entity a, Entity b) => Allies(Of(a), Of(b));

    /// <summary>True if <paramref name="a"/> and <paramref name="b"/> are on different teams (see <see cref="Of"/>).</summary>
    public static bool Foes(Entity a, Entity b) => Foes(Of(a), Of(b));
}

/// <summary>
/// Puts an entity, and everything inside it, on a team: any genre's team, the same one a turn order's Teams, a strategy
/// faction or a network game's Teams name. Allies don't hurt each other (touch damage, bumps, hitboxes, weapons), are
/// friends to tactics units and card hands of their team, take turns on their team's turn, and collect and pay with
/// their team's items (an <see cref="Inventory"/> whose For Team is theirs, or their strategy faction's resources). Give
/// it to what has no team of its own: a platformer hero joining a tactics side, a boss a match-3 board fights.
/// </summary>
[Category("Gameplay")]
[ComponentName("Griff.Team")]
public sealed class Team : Component, ITeamMember
{
    /// <summary>The team's name, e.g. "Blue" or "Player". Empty puts it on no team.</summary>
    [Names(NameKind.Kind, Scope = Teams.Scope, Defines = true)]
    public string? Name { get; set; } = "Player";

    string? ITeamMember.Team => Name;
}
