using Griff.Runtime;

namespace Griff.Modules.Fighting;

/// <summary>What a script can ask of a fighting game, as members of <see cref="Game"/>: <c>Game.ShowHitboxes</c>.</summary>
public static class FightingGame
{
    extension(Game game)
    {
        /// <summary>
        /// Draws every <see cref="Hitboxes"/>' boxes in play over the game: hitboxes red, hurtboxes blue (yellow in hitstun),
        /// hitboxes that have hit already this swing faint. The editor's Game &gt; Show Hitboxes sets it; a training mode can too.
        /// </summary>
        public bool ShowHitboxes
        {
            get => Of(game).Show;
            set => Of(game).Show = value;
        }
    }

    // Every game runs the module's system.
    internal static HitboxesSystem Of(Game game) =>
        game.GetSystem<HitboxesSystem>() ?? throw new InvalidOperationException("The game isn't running the Fighting module.");
}
