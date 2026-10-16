using System.Numerics;
using Griff.Editing;
using Griff.Scenes;

namespace Griff.Modules.Cards;

/// <summary>
/// Creates a deck of twenty cards, Sun and Moon from 1 to 10, on the left of the table, shuffled as the game starts. A scene
/// with no hand also gets one along the bottom that draws five cards from it, a play zone in the middle to play them onto
/// and a discard pile on the right (which the deck shuffles back in when it runs out), so it plays straight away: drag a
/// card onto the zone, or click the deck to draw. All in one undo step.
/// </summary>
[Category("Cards")]
public sealed class DeckPreset : EntityPreset
{
    public override string Id => "Entity.CreateDeck";

    public override string Text => "Create Deck";

    public override int Order => 26;

    public override string? Icon => "Copy";

    public override string EntityName(Scene scene) => CardNames.Free(scene, "Deck");

    // Beside whatever's picked, never inside a card or another deck.
    public override Entity? InsideOf(Entity? selected) => CardNames.Outside(selected);

    public override void Configure(Entity entity, Scene scene)
    {
        var deck = entity.AddComponent<Deck>();
        deck.RefillFrom = CardNames.Pile(scene) ?? "Discard Pile";
        entity.Transform.Position = new Vector3(-6.5f, 0.9f, 0);
    }

    public override IEnumerable<Entity> CreateChildren(Scene scene)
    {
        foreach (var (kind, color) in new[] { ("Sun", Color.FromBytes(0xF6, 0xE7, 0xB8)), ("Moon", Color.FromBytes(0xD9, 0xE4, 0xF5)) })
        {
            for (var value = 1; value <= 10; value++)
            {
                var entity = new Entity($"{kind} {value}");
                var card = entity.AddComponent<Card>();
                (card.Title, card.Kind, card.Value, card.Color, card.FaceUp) = (kind, kind, value, color, false);
                yield return entity;
            }
        }
    }

    public override IEnumerable<IEdit> EditScene(Scene scene)
    {
        if (scene.Entities.Any(e => e.GetComponent<Hand>() is not null))
            yield break;
        var deckName = EntityName(scene);
        var pileName = CardNames.Pile(scene);
        if (pileName is null)
        {
            pileName = CardNames.Free(scene, "Discard Pile");
            var pile = new Entity(pileName);
            var discard = pile.AddComponent<Deck>();
            (discard.FaceUp, discard.TakesDrops, discard.ShuffleOnStart, discard.ClickToDraw) = (true, true, false, false);
            pile.Transform.Position = new Vector3(6.5f, 0.9f, 0);
            yield return new AddEntityEdit(scene, pile);
        }

        if (!scene.Entities.Any(e => e.GetComponent<PlayZone>() is not null))
        {
            var table = new Entity(CardNames.Free(scene, "Play Zone"));
            var zone = table.AddComponent<PlayZone>();
            (zone.DiscardPile, zone.Label) = (pileName, "Play a card here");
            table.Transform.Position = new Vector3(0, 0.9f, 0);
            yield return new AddEntityEdit(scene, table);
        }

        var holder = new Entity(CardNames.Free(scene, "Hand"));
        var hand = holder.AddComponent<Hand>();
        (hand.Deck, hand.DiscardPile) = (deckName, pileName);
        holder.Transform.Position = new Vector3(0, -3.5f, 0);
        yield return new AddEntityEdit(scene, holder);
    }
}

/// <summary>
/// Creates a play zone: a place on the table, 9 by 2.8, that cards from any hand are played onto in a row by dragging
/// them there. Set what it Accepts and its Limit to make rules of it.
/// </summary>
[Category("Cards")]
public sealed class PlayZonePreset : EntityPreset
{
    public override string Id => "Entity.CreatePlayZone";

    public override string Text => "Create Play Zone";

    public override int Order => 27;

    public override string? Icon => "Zone";

    public override string EntityName(Scene scene) => CardNames.Free(scene, "Play Zone");

    public override Entity? InsideOf(Entity? selected) => CardNames.Outside(selected);

    public override void Configure(Entity entity, Scene scene)
    {
        var zone = entity.AddComponent<PlayZone>();
        zone.DiscardPile = CardNames.Pile(scene);
        zone.Label = "Play a card here";
        var others = scene.Entities.Count(e => e.GetComponent<PlayZone>() is not null);
        entity.Transform.Position = new Vector3(0, 0.9f + 3.2f * others, 0);
    }
}

/// <summary>
/// Creates a card: inside the deck, hand or play zone picked (or one a card picked is in), otherwise on its own. Set its
/// Title, Text, Value, Kind and Color in the Inspector.
/// </summary>
[Category("Cards")]
public sealed class CardPreset : EntityPreset
{
    public override string Id => "Entity.CreateCard";

    public override string Text => "Create Card";

    public override int Order => 28;

    public override string? Icon => "Card";

    public override string EntityName(Scene scene) => CardNames.Free(scene, "Card");

    // Into the zone picked, or the one the picked card is in.
    public override Entity? InsideOf(Entity? selected)
    {
        for (var e = selected; e is not null; e = e.Parent)
        {
            if (e.GetComponent<CardZone>() is not null)
                return e;
        }

        return CardNames.Outside(selected);
    }

    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<Card>().Value = 1;
}

/// <summary>Names the card presets give.</summary>
internal static class CardNames
{
    /// <summary><paramref name="name"/>, or with a number after it if the scene has one already ("Deck 2").</summary>
    public static string Free(Scene scene, string name)
    {
        var taken = scene.Entities.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(name))
            return name;
        for (var i = 2; ; i++)
        {
            if (!taken.Contains($"{name} {i}"))
                return $"{name} {i}";
        }
    }

    /// <summary>The scene's first face-up deck (a discard pile), or null.</summary>
    public static string? Pile(Scene scene) => scene.Entities.FirstOrDefault(e => e.GetComponent<Deck>() is { FaceUp: true })?.Name;

    /// <summary>What a new deck or zone goes inside, given what's picked: not a card or another place cards go.</summary>
    public static Entity? Outside(Entity? selected)
    {
        var parent = selected;
        while (parent is not null && (parent.GetComponent<Card>() is not null || parent.GetComponent<CardZone>() is not null))
            parent = parent.Parent;
        return parent;
    }
}
