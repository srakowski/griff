using Griff.Runtime;

namespace Griff.Modules.Fighting;

/// <summary>What a script can ask of a fighting game, as members of <see cref="Game"/>: <c>Game.ShowHitboxes</c>.</summary>
public static class FightingGame
{
    /// <summary>
    /// What the hitboxes' drawing is called (see <see cref="IDebugDrawing"/>): <c>Game.ShowDebugDrawing("Hitboxes", true)</c>
    /// is <c>Game.ShowHitboxes = true</c>, and the editor's Game &gt; Show Hitboxes switches it by this name.
    /// </summary>
    public const string HitboxesDrawing = "Hitboxes";

    extension(Game game)
    {
        /// <summary>
        /// Draws every <see cref="Hitboxes"/>' boxes in play over the game: hitboxes red, hurtboxes blue (yellow in hitstun),
        /// hitboxes that have hit already this swing faint. The editor's Game &gt; Show Hitboxes sets it; a training mode can too.
        /// A game that doesn't run the Fighting module (its project has no fighter) has none to show: it's false there, and
        /// setting it does nothing.
        /// </summary>
        public bool ShowHitboxes
        {
            get => game.GetSystem<HitboxesSystem>()?.Show ?? false;
            set
            {
                if (game.GetSystem<HitboxesSystem>() is { } system)
                    system.Show = value;
            }
        }
    }
}
