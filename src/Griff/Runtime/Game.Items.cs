using System.Globalization;
using System.Numerics;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for items: each member forwards to ItemSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>
    /// How many of <paramref name="item"/> <paramref name="entity"/> has to pay with: in what holds items on it or the
    /// entity it's inside (an <see cref="Inventory"/>, a card hand's mana), else its team's (see <see cref="Items"/>).
    /// </summary>
    public int CountItems(Entity? entity, string item) =>
        _itemUses.CountItems(entity, item);

    /// <summary>True if <paramref name="entity"/> can pay <paramref name="cost"/>, e.g. "150 Gold, 50 Wood" (see <see cref="CountItems"/>).</summary>
    public bool CanPay(Entity? entity, string? cost) =>
        _itemUses.CanPay(entity, cost);

    /// <summary>Pays <paramref name="cost"/> from <paramref name="entity"/>'s items (or its team's) if it can; returns whether it did.</summary>
    public bool Pay(Entity? entity, string? cost) =>
        _itemUses.Pay(entity, cost);

    /// <summary>
    /// Gives <paramref name="entity"/> <paramref name="count"/> of <paramref name="item"/> (takes them, for less than 0): into
    /// what holds that item for it or its team, or an <see cref="Inventory"/> added to it if nothing does.
    /// </summary>
    public void Give(Entity entity, string item, int count = 1) =>
        _itemUses.Give(entity, item, count);
}
