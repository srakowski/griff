using System.Globalization;
using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for turns: each member forwards to TurnSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>The turn order <paramref name="entity"/> takes turns in (see <see cref="TurnOrder.Within"/>), if any.</summary>
    public TurnOrder? TurnOrderOf(Entity? entity) =>
        _turns.TurnOrderOf(entity);

    /// <summary>The turn orders running this frame, in the scene's order.</summary>
    public ComponentList<TurnOrder> TurnOrdersNow => _turns.TurnOrdersNow;

    /// <summary>
    /// The team a turn taker acts for: its own, else the team it's on some other way (a Team on it or above it), or in a
    /// network game with teams, its player's team.
    /// </summary>
    public string TeamOf(TurnTaker taker) =>
        _turns.TeamOf(taker);

    /// <summary>
    /// The team <paramref name="entity"/> is on (see <see cref="Teams.Of"/>), or in a network game with teams, the team of
    /// the player who plays it. Empty for none.
    /// </summary>
    public string TeamOf(Entity? entity) =>
        _turns.TeamOf(entity);

    /// <summary>True if <paramref name="entity"/> acts for a turn taker that has to wait: it isn't its turn, or it has used its actions.</summary>
    public bool Waiting(Entity? entity) =>
        _turns.Waiting(entity);

    /// <summary>
    /// An attack swung or a shot fired on its turn uses one of its turn taker's actions.
    /// </summary>
    public void UseAction(Entity entity) =>
        _turns.UseAction(entity);
}
