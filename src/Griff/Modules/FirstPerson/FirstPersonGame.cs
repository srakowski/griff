using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.FirstPerson;

/// <summary>
/// What a script can ask of a first-person game, as members of <see cref="Game"/> (<c>Game.FirstPersonViewer</c>,
/// <c>Game.Equip(shotgun)</c>): the view, the player looked through, and the weapon in hand.
/// </summary>
public static class FirstPersonGame
{
    extension(Game game)
    {
        /// <summary>The camera's <see cref="FirstPerson.FirstPersonView"/>, if the game is played in first person.</summary>
        public FirstPersonView? FirstPersonView => Of(game).FirstPersonView;

        /// <summary>The entity a <see cref="FirstPerson.FirstPersonView"/> looks through (the player), if there is one.</summary>
        public Entity? FirstPersonViewer => Of(game).FirstPersonViewer;

        /// <summary>The weapon the first-person player has out, if any (see <see cref="Weapon"/>).</summary>
        public Weapon? CurrentWeapon => Of(game).CurrentWeapon;

        /// <summary>Takes out <paramref name="weapon"/> (one of the player's), if it can be used; returns whether it did.</summary>
        public bool Equip(Weapon weapon) => Of(game).Equip(weapon);

        /// <summary>
        /// Fires <paramref name="weapon"/> once from its owner the way the owner faces, whether or not Fire is held and
        /// without using ammo: for scripts and cutscenes.
        /// </summary>
        public void Fire(Weapon weapon) => Of(game).Fire(weapon);
    }

    // Every game runs the module's system.
    internal static FirstPersonSystem Of(Game game) =>
        game.GetSystem<FirstPersonSystem>() ?? throw new InvalidOperationException("The game isn't running the First Person module.");
}
