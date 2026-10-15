namespace Griff.Scenes;

/// <summary>
/// Which of the people playing at this one machine plays the entity (and everything inside it): Player 1, 2, 3 or 4, each
/// with their own keys or controller. Two players' characters in one scene, each with a Played By, are played by two people
/// at once: at one keyboard (one on WASD, one on the arrows), on two controllers, or one at the keyboard and one on a
/// controller (see <c>Game.LocalPlayers</c>). Its movement, attacks and moves read that player's controls alone; everything
/// without one, menus included, answers to everyone.
/// </summary>
/// <remarks>
/// With one player in the scene (no Played By naming a second), that player has every key and controller. In a network
/// game a player's own character answers to them, as it always does; Played By shares out the controls of the machine the
/// game is run on among the people at it.
/// </remarks>
[Category("Multiplayer")]
[ComponentName("Griff.PlayedBy")]
public sealed class PlayedBy : Component
{
    /// <summary>The most people that can play at one machine.</summary>
    public const int MaxPlayers = 4;

    /// <summary>Which player at this machine plays it, 1 to 4.</summary>
    public int Player { get; set; } = 1;

    /// <summary>The player at this machine a <see cref="PlayedBy"/> on <paramref name="entity"/> (or what it's inside) names, or 0 for none.</summary>
    public static int Of(Entity? entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<PlayedBy>() is { } played)
                return Math.Clamp(played.Player, 1, MaxPlayers);
        }

        return 0;
    }
}
