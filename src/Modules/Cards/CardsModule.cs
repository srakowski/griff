using Griff.Runtime;

namespace Griff.Modules.Cards;

/// <summary>
/// Card games: cards (<see cref="Card"/>) drawn from decks (<see cref="Deck"/>) into hands (<see cref="Hand"/>) and played
/// by dragging them onto play zones (<see cref="PlayZone"/>), shuffled, discarded and sliding between them; the Cards
/// template, a duel against the computer made from a table of cards; and Create Deck, Create Play Zone and Create Card in
/// the Entity menu.
/// </summary>
public sealed class CardsModule : GameModule
{
    public override string Name => "Cards";

    protected override void AddTo(GameSetup game)
    {
        // After scripts (so a card a script plays goes this frame) and after dragging.
        var cards = game.AddSystem(new CardsSystem());
        game.StepAfter(Steps.Dragging, "Cards", cards, cards.Run);
        game.OnNetCommand(CardsSystem.CardsCommand, cards.RunCommand);
    }
}
