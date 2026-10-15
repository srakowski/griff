---
title: "Card games"
description: "Cards, decks, hands and play zones: draw, shuffle, discard, and play cards by dragging them, with no code."
---

## Decks, hands and play zones

**Entity → Create Deck** makes a card game you can play straight away: a **Deck** of twenty cards on the left (Sun and Moon, 1 to 10), and in a scene with no hand yet, a **Hand** along the bottom that draws five of them as the game starts, a **Play Zone** in the middle and a **Discard Pile** on the right. Play, and the deck is shuffled, five cards are dealt into the hand one after another and fan out, and you drag a card onto the zone to play it: it slides into place there. Click the deck to draw another. **Entity → Create Play Zone** adds more places to play onto, and **Entity → Create Card** a card (inside the deck, hand or zone picked). All of it works from the Inspector, with no code.

A card goes wherever its entity is: the cards of a deck, a hand or a play zone are the entities inside it with a **Card**, in order (the last is a deck's top card and a hand's rightmost). Each of those places lays its cards out, and a card that moves from one to another, or along to make room, slides there over its **Slide Time**, drawn over everything on its way.

## Cards

A **Card** draws itself: a card of its **Color** with its **Title** along the top (empty writes its entity's name), its **Value** in the top-left corner (0 shows none), a picture (**Image**), its rules **Text** wrapped to fit, and its **Kind** at the bottom. A card with no picture and no text shows its value big in the middle, like a playing card. **Text Color**, **Size** (`1.6, 2.24`, a playing card's shape) and **Font** finish it. Give it a **Sprite** instead and the sprite is its face. **Face Up** says whether its face shows (decks turn their cards face down, hands face up), and its back is its **Home** deck's (the deck it belongs to, set as the game starts for the cards in each deck). Its **Kind** (`Spell`, `Hearts`) is what play zones check before they take it, and its Value is an output, for a Text or a Counter.

Components on a card's entity that are an **ICardPart** hear when the card is drawn, played and discarded, and can say it can't be played (`CanPlay`): card effects and costs hang off a card that way, a script component as much as one of Griff's.

## Decks

A **Deck** is a pile of cards to draw from, shuffled as the game starts (**Shuffle On Start**). Shuffles use the game's random numbers (`Rand`), so a game seeded with `Rand.Seed` deals the same every time. Face down it shows its back (its **Back** picture, or a plain back of **Back Color**), a little thicker for each card in it (**Spread**); with **Face Up** ticked it's a discard pile showing its top card. **Click To Draw** draws its top card into your hand when you click it. **Takes Drops** lets cards be dragged from a hand onto it: a discard pile. **Refill From** names a discard pile whose cards are shuffled back in when a card is to be drawn and it has run out; with nothing to refill from it sends its **Empty Message**. It plays its **Draw Sound** and **Shuffle Sound** (and sends its **Shuffle Message**), and gives a little shake as it's shuffled.

Its actions, for a **Trigger**, a wire or a script: **Draw** (a number of cards, into the hand that draws from it, the one whose side's turn it is if several do), **Shuffle**, and **Restart**, which gathers every card that belongs to it back in face down, shuffles, and has the hands that draw from it deal afresh: a new game. Its outputs: **Count** (cards in it) and **Shuffles**.

## Decks from a table

A deck can be made from a [table](/docs/data-tables/): set its **Table** (`Cards`) and as the game starts it makes a card for each row, named like the row, with a **Table Values** giving it the row's values. Columns named like a card's properties set them (**Title**, **Text**, **Value**, **Kind**, **Color** as `#FF004D`, **Image**), and the **Count Column** (`Count`, a whole number) says how many copies of each row's card go in. **Prefab** is the prefab each card is a copy of (empty makes plain cards), so a card prefab with a script or other components on it gets their properties from the table's columns of the same names too. A table or prefab that isn't there is said in Output as the game starts. The cards are made once (the deck's **Started** keeps a rewind from making them again), so balancing the table while the game runs changes the cards already made, as Table Values always does.

## Hands

A **Hand** holds a player's cards, fanned out about its middle: **Spacing** apart while there's room, squeezed closer to fit **Max Width**, each turned by **Fan** degrees from the next and dipping by **Arc** at the ends. It draws **Start With** cards from its **Deck** (empty: the scene's first face-down deck) as the game starts, one after another, and no more than its **Limit**. Hovering a card lifts it out of the fan (**Hover Rise**, drawn **Hover Scale** times bigger).

- **With the mouse**: drag a card onto a play zone that takes it to play it (the zone lights up in its **Hover Color** while you hold one over it, and the zones it could go to faintly); let it go anywhere else and it slides back. Or click a card to pick it up (outlined in **Highlight Color**) and then click the zone. Right-click, or click the card again, puts it back.
- **On a touch screen**: tap a card, then tap the zone, or drag it there.
- **With the keys or a gamepad**: left and right (the arrows, A and D, the d-pad or the stick) move along the cards, the **Pick Action** (`Jump`: Space or A) takes one up, left and right choose a zone it can go to, and the Pick Action again plays it there. The **Cancel Action** (`Dash`: Shift or B) puts it back, and the **Draw Action** (`Interact`: E, Enter or Y) draws from its deck if the deck has Click To Draw.

Its actions: **Draw** (a number of cards from its deck) and **Discard** (every card in it onto its **Discard Pile**, empty for the scene's first face-up deck). Its output: **Count**. It plays its **Pick Sound** as a card is taken up and sends its **Draw Message** with each card drawn. A Trigger makes the rules of a turn: draw a card as each turn starts ("when `BlueTurn` arrives, Draw on the Hand"), or discard the hand as it ends.

## Play zones

A **Play Zone** is a place on the table, **Size** across, drawn in its **Color** with its **Label** while it's empty. Its rules: it **Accepts** only the kinds of card named (`Creature, Spell`; empty takes any), up to its **Limit** (0 for any number), and only from the hands named in **From** (empty: any), so each side can have its own row; a card it won't take slides back to the hand, with its **Refuse Sound**. It lays the cards played onto it out in a **Row** (side by side, **Spacing** apart, squeezed to fit) or a **Pile** (each on the last: a trick), **Face Up** or face down (a trap, a hidden bet). Each card played plays its **Play Sound** and sends its **Play Message** with the card as its data. Its action **Discard** puts every card on it onto its **Discard Pile**, clearing the table at the end of a round. Its outputs: **Count** (cards on it) and **Played** (cards played onto it all told).

## Turns, focus and sides

Give a hand a **Turn Taker** naming its side and it's played only on its side's turn: with **Actions** (`1`), each card played uses one, and once they're used the turn passes. A hand on a side the turn order's **Computer Sides** names isn't played with the mouse or keys. **Hidden** shows a hand's faces only to its own side: at one screen, the computer's hand is drawn face down, and with two people taking turns at one screen (`Blue, Red`, no Computer Sides) each hand is face down while it isn't its side's turn, so they can pass the keyboard without peeking. Untick a hand's **Controllable** (or **Focus** something else) and it ignores the mouse and keys while its actions and scripts still work, so a card game can sit in a corner of another game (see [Mixing genres](/docs/mashups/)).

## Rewinds and network games

Which cards are where, in what order, which way up, and the decks' and hands' state are all saved with the scene, so a rewind goes back to them. In a network game the host shuffles, deals and plays: a player's drags and clicks are sent to it, and only the players on a hand's side may play it (its Turn Taker's side, matched against the **Multiplayer**'s **Teams**). Nobody can peek: the cards in a face-down deck aren't sent to anyone's machine (only how many there are), and the cards in a hidden hand aren't sent to the players on other sides, whose machines draw backs in their place. A card is sent to everyone once it's played face up or discarded face up.

## Scripting

Scripts read a zone's `Cards` and `Count`, a deck's `Top`, a card's `Zone`, `IsResting` and `ShownTitle`, and a hand's `Picked` (the card this player has taken up) and `Side`. They draw with `Draw(count)`, shuffle with `Shuffle()`, play a card with `hand.Play(card, zone)` and discard one with `hand.Discard(card)`, and hear a card's `Drawn`, `Played` and `Discarded`, a hand's `Drew`, a zone's `CardPlayed` and a deck's `Shuffled`. A component of their own on a card's entity that is an `ICardPart` is told what happens to the card, and its `CanPlay` can keep it in the hand:

```csharp
public class Cost : Behavior, ICardPart
{
    public static int Mana { get; set; } = 3;

    public int Price { get; set; } = 2;

    public bool CanPlay(Card card, PlayZone zone) => Mana >= Price;

    public void Played(Card card, PlayZone zone) => Mana -= Price;
}
```
