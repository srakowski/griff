namespace Griff.Scenes;

/// <summary>
/// Paints the <see cref="Sprite"/> on this entity in the color of the team of whoever it's part of: a strategy unit's or
/// building's faction color, a network game's player color (see Network Settings' Color Players), so a band or banner on a
/// character shows whose it is. Put it on a child with the sprite; a character with none has its own sprite painted.
/// </summary>
[Category("Gameplay")]
[ComponentName("Griff.TeamColor")]
public sealed class TeamColor : Component
{
    /// <summary>What entities called this were before the component existed: scenes saved then get one (see the scene loader).</summary>
    internal const string FormerEntityName = "Team Color";

    /// <summary>The sprites under <paramref name="entity"/> (and its own) that take its team's color.</summary>
    public static List<Sprite> SpritesIn(Entity entity) =>
        [.. entity.SelfAndDescendants().Where(e => e.GetComponent<TeamColor>() is not null).SelectMany(e => e.GetComponents<Sprite>())];
}
