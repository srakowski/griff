---
title: "Card games"
description: "Cards, decks, hands and play zones: draw, shuffle, discard, and play cards by dragging them; cards that deal damage, heal, draw and cost mana; a computer opponent; and the Cards template, a whole duel, with no code."
---

**File → New Project** and the **Cards** template make a card duel against the computer to play straight away and build on: see [The Cards template](#the-cards-template).

## Decks, hands and play zones

**Entity → Genres → Cards → Deck** makes a card game you can play straight away: a **Deck** of twenty cards on the left (Sun and Moon, 1 to 10), and in a scene with no hand yet, a **Hand** along the bottom that draws five of them as the game starts, a **Play Zone** in the middle and a **Discard Pile** on the right. Play, and the deck is shuffled, five cards are dealt into the hand one after another and fan out, and you drag a card onto the zone to play it: it slides into place there. Click the deck to draw another. **Entity → Genres → Cards → Play Zone** adds more places to play onto, and **Entity → Genres → Cards → Card** a card (inside the deck, hand or zone picked). All of it works from the Inspector, with no code.

A card goes wherever its entity is: the cards of a deck, a hand or a play zone are the entities inside it with a **Card**, in order (the last is a deck's top card and a hand's rightmost). Each of those places lays its cards out, and a card that moves from one to another, or along to make room, slides there over its **Slide Time**, drawn over everything on its way.

## Cards

A **Card** draws itself: a card of its **Color** with its **Title** along the top (empty writes its entity's name), its **Value** in the top-left corner (0 shows none), a picture (**Image**), its rules **Text** wrapped to fit, and its **Kind** at the bottom. A card with no picture and no text shows its value big in the middle, like a playing card. **Text Color**, **Size** (`1.6, 2.24`, a playing card's shape) and **Font** finish it. Give it a **Sprite** instead and the sprite is its face. **Face Up** says whether its face shows (decks turn their cards face down, hands face up), and its back is its **Home** deck's (the deck it belongs to, set as the game starts for the cards in each deck). Its **Kind** (`Spell`, `Hearts`) is what play zones check before they take it, and its Value is an output, for a Text or a Counter.

Its **Cost** (shown in a blue gem in its top-right corner) is the mana it takes to play (see [Mana](#mana)), and what it does when it's played is its **Card Effects** (see [Card effects](#card-effects)). With no Text of its own, a card writes what its effects do on its face ("Deal 3 damage."). Components on a card's entity that are an **ICardPart** hear when the card is drawn, played and discarded, and can say it can't be played (`CanPlay`): a Card Effect is one, and a script component can be one too.

A card with a **Health** of its own is a creature (see [Card effects](#card-effects)): it shows its health in a red gem in its bottom-right corner (pale once it's hurt), flashes and shakes when it's hit on the table (unless its Health's **Flash On Hit** is unticked), and lunges at what it strikes as its side's turn starts.

## Decks

A **Deck** is a pile of cards to draw from, shuffled as the game starts (**Shuffle On Start**). Shuffles use the game's random numbers (`Rand`), so a game seeded with `Rand.Seed` deals the same every time. Face down it shows its back (its **Back** picture, or a plain back of **Back Color**), a little thicker for each card in it (**Spread**); with **Face Up** ticked it's a discard pile showing its top card. **Click To Draw** draws its top card into your hand when you click it. **Takes Drops** lets cards be dragged from a hand onto it: a discard pile. **Refill From** names a discard pile whose cards are shuffled back in when a card is to be drawn and it has run out; with nothing to refill from it sends its **Empty Message**. It plays its **Draw Sound** and **Shuffle Sound** (and sends its **Shuffle Message**), and gives a little shake as it's shuffled.

Its actions, for a **Trigger**, a wire or a script: **Draw** (a number of cards, into the hand that draws from it, the one whose side's turn it is if several do), **Shuffle**, and **Restart**, which gathers every card that belongs to it back in face down, shuffles, and has the hands that draw from it deal afresh: a new game. Its outputs: **Count** (cards in it) and **Shuffles**.

## Decks from a table

A deck can be made from a [table](/docs/data-tables/): set its **Table** (`Cards`) and as the game starts it makes a card for each row, named like the row, with a **Table Values** giving it the row's values. Columns named like a card's properties set them (**Title**, **Text**, **Value**, **Kind**, **Color** as `#FF004D`, **Image**), and the **Count Column** (`Count`, a whole number) says how many copies of each row's card go in. **Prefab** is the prefab each card is a copy of (empty makes plain cards), so a card prefab with a script or other components on it gets their properties from the table's columns of the same names too. A row can name its own prefab in the **Prefab Column** (`Prefab`), so one table holds cards of several makes: `Creature` for a card with a Health, `Spell` for one without (a row with it empty uses the deck's). A table or prefab that isn't there is said in Output as the game starts. The cards are made once (the deck's **Started** keeps a rewind from making them again), so balancing the table while the game runs changes the cards already made, as Table Values always does.

## Hands

A **Hand** holds a player's cards, fanned out about its middle: **Spacing** apart while there's room, squeezed closer to fit **Max Width**, each turned by **Fan** degrees from the next and dipping by **Arc** at the ends. It draws **Start With** cards from its **Deck** (empty: the scene's first face-down deck) as the game starts, one after another, and no more than its **Limit**. Hovering a card lifts it out of the fan (**Hover Rise**, drawn **Hover Scale** times bigger).

- **With the mouse**: drag a card onto a play zone that takes it to play it (the zone lights up in its **Hover Color** while you hold one over it, and the zones it could go to faintly); let it go anywhere else and it slides back. Or click a card to pick it up (outlined in **Highlight Color**) and then click the zone. Right-click, or click the card again, puts it back.
- **On a touch screen**: tap a card, then tap the zone, or drag it there.
- **With the keys or a gamepad**: left and right (the arrows, A and D, the d-pad or the stick) move along the cards, the **Pick Action** (`Jump`: Space or A) takes one up, left and right choose a zone it can go to, and the Pick Action again plays it there. The **Cancel Action** (`Dash`: Shift or B) puts it back, and the **Draw Action** (`Interact`: E, Enter or Y) draws from its deck if the deck has Click To Draw.

Its actions: **Draw** (a number of cards from its deck) and **Discard** (every card in it onto its **Discard Pile**, empty for the scene's first face-up deck). Its output: **Count**. It plays its **Pick Sound** as a card is taken up and sends its **Draw Message** with each card drawn. A Trigger makes the rules of a turn: draw a card as each turn starts ("when `BlueTurn` arrives, Draw on the Hand"), or discard the hand as it ends.

## Play zones

A **Play Zone** is a place on the table, **Size** across, drawn in its **Color** with its **Label** while it's empty. Its rules: it **Accepts** only the kinds of card named (`Creature, Spell`; empty takes any), up to its **Limit** (0 for any number), and only from the hands named in **From** (empty: any), so each side can have its own row; a card it won't take slides back to the hand, with its **Refuse Sound**. It lays the cards played onto it out in a **Row** (side by side, **Spacing** apart, squeezed to fit) or a **Pile** (each on the last: a trick), **Face Up** or face down (a trap, a hidden bet). Each card played plays its **Play Sound** and sends its **Play Message** with the card as its data. Its action **Discard** puts every card on it onto its **Discard Pile**, clearing the table at the end of a round. With **Discard After** (seconds), a card played onto it goes on to the discard pile by itself that long after it has slid into place: a spell, cast and gone. The cards that stay on the table to do something stay: a creature (a card with a Health) and a card with an effect that acts each turn. Its outputs: **Count** (cards on it) and **Played** (cards played onto it all told).

## Turns, focus and sides

Give a hand a **Turn Taker** naming its side and it's played only on its side's turn: with **Actions** (`1`), each card played uses one, and once they're used the turn passes. A hand on a side the turn order's **Computer Sides** names isn't played with the mouse or keys. **Hidden** shows a hand's faces only to its own side: at one screen, the computer's hand is drawn face down, and with two people taking turns at one screen (`Blue, Red`, no Computer Sides) each hand is face down while it isn't its side's turn, so they can pass the keyboard without peeking. Untick a hand's **Controllable** (or **Focus** something else) and it ignores the mouse and keys while its actions and scripts still work, so a card game can sit in a corner of another game (see [Mixing genres](/docs/mashups/)).

## Card effects

A **Card Effect** on a card says what it does, with no code. Give a card one for each thing it does:

- **Damage** takes hit points from its target's **Health**, **Heal** gives them back (never past its Max). Either can be dice instead (**Damage Roll** `1d6+1`), rolled with the game's random numbers each time.
- **Draw** has the player who played it draw that many cards; **Discard** has its target's side discard that many from their hand, picked at random.
- **Gain Mana** gives the player who played it more mana this turn (a coin).
- **Buff** adds to a number of its target's for good: **Buff Stat** names it, `Value` (a card's), `Armor`, or `Health.Max` for one component's. Less than 0 weakens.
- **Do** does any action, as a [Trigger](/docs/story/#triggers) does (**With** is its value): on its target, or on the entities **Do On** names. A card can open a door in the level, start a spawner, end a turn or run a script's own action, so a card game can drive the rest of a game (see [Mixing genres](/docs/mashups/)).
- **Sound** plays and **Effect** (a prefab) is spawned on each target as it happens.

Its **Target** is whom it's done to: the **Opponent** (the other side's hero, and their hand for a discard), **Self** (your own hero and hand), a **Chosen** card or character the player picks as they play it, **All Foes**, **All Friends** or **Everyone**. **When** it happens: as the card is **Played** (the usual), **Drawn** (a curse that hurts you as it comes), **Discarded**, or **Each Turn**: as each of its side's turns starts while it's on the table, a creature that strikes every turn (it lunges at what it strikes, and its hand's **Said** says what it did: "Wolf: 2 damage to Red Hero.").

A side's life is its hero's Health: the entity the hand's **Hero** names, or with none, the hand itself if it has a Health, or the first character with a Health and a **Turn Taker** on the hand's side. A card on the table with a Health of its own is a creature: effects can target it, and when it dies it goes to its zone's discard pile, mended for the next time it's played (untick its Health's **Destroy On Death**, or it's gone for good). Damage from cards always lands, even while a Health can't be hurt after its last hit, so two quick cards both count. Whose side a creature is on is the hand it was played from (its **Played By**).

### Choosing a target

A card whose effect targets **Chosen** waits to be aimed once it's let go on a play zone: a dotted line runs from it to the mouse, and what it can be aimed at is outlined. Click a card on the table or a character to play it there; click anywhere else, or right-click, to put it back. Let it go straight onto what it's aimed at and it's played there at once. With the keys or a gamepad, left and right choose among what it can be aimed at (starting on what the computer would pick) and the Pick Action plays it; the Cancel Action puts it back. **Choose From** keeps the choice to the entities named or what's inside them (`Enemy Row`: only the cards on it). A card with nothing to aim at can't be played.

## Mana

A hand's **Mana** pays its cards' costs: a card that costs more than it has left can't be played, and its cost is shown dimmed. The hand fills back up to its **Max Mana** as each of its side's turns starts (with a Turn Order), and **Mana Growth** adds to Max Mana each turn up to **Mana Limit**: Max Mana 0 and Mana Growth 1 starts at one and adds a crystal a turn. Max Mana 3 with no growth is the same three every turn. A hand with mana shows it in a blue gem beside it, "2/3", and its Mana and Max Mana are outputs for a Text or a Bar. Its **Gain Mana** action gives it more (picking up a coin in another part of the game).

## Cards from a table

A deck made from a [table](/docs/data-tables/) can carry its cards' effects and costs as columns: give its **Prefab** a Card Effect, and columns called **Damage**, **Heal**, **Draw**, **Discard**, **Gain Mana**, **Target** and **Cost** set them on each card (a dice column for Damage rolls for each play). A table like this is a whole card set with no code:

| Name | Title | Cost | Damage | Heal | Target | Count |
| --- | --- | --- | --- | --- | --- | --- |
| Strike | Strike | 1 | 1d4+1 | | Chosen | 4 |
| Salve | Salve | 2 | | 3 | Self | 2 |
| Quake | Quake | 3 | 2 | | Everyone | 1 |

Creatures come from the same table with a **Prefab** column: a `Creature` prefab with a Health and a Card Effect whose When is Each Turn, and a Table Values whose **Sets** has `Health.Max = Health` among its lines, takes a **Health** column for how tough each one is, and its Damage or Heal for what it does every turn.

## Playing against the computer

A hand on a side its Turn Order's **Computer Sides** names is played by the computer on its turn, from its hidden hand. It takes its **Think Time** before each play, the last half of it showing the card it's about to play lifted out of its hand (and, for a chosen target, a line to what it's aiming at), so the player can follow what it does. It plays by a rule you can predict:

1. a card that finishes off the opponent, if it has one;
2. its healing, once its hero is below **Heal Below** of its health (it keeps its healing cards until then);
3. the card that deals the most damage;
4. a card that does something else (draws, mana, a buff, an action, or acts each turn);
5. a plain card with no effects, highest Value first.

Ties go to the costlier card, then the leftmost. It plays only what it can pay for, and never below **Keep** cards in its hand. It aims damage at a foe it can finish off, else the opponent's hero, else the weakest foe, and healing at the friend missing the most health. Once it has nothing it wants to play it ends its turn (set the Turn Order's **Computer Turn Time** to 0, or long enough for all its plays). There's no luck in its choices, so a game seeded with `Rand.Seed` plays out the same every time. Its hand's **Said** output says what it last did, "Red plays Fireball: 4 damage to Blue Hero.", for a Text to show (a person's plays say it too).

## The Cards template

**File → New Project** and the **Cards** template make a card duel against the computer, with no code. Its menu has **Play** and **Quit**, over three cards of the set; Play opens the `Duel` scene, a card table of green felt with your mage on the left at the bottom and the computer's lich above it, 20 health each. You go first.

- **The deck** on the right is made from the **Cards** table (Data → Open Table → Cards): sixteen cards, each a row with its **Title**, **Kind**, **Cost**, **Damage**, **Heal**, **Draw**, **Gain Mana**, **Target**, **Color**, **Image**, the **Effect** prefab spawned where it lands, its **Sound**, how many go in (**Count**), and which **Prefab** it's made from. Creatures (Slime, Goblin, Rogue, Skeleton, Cleric, Knight, Orc and Wyvern) have a **Health** and strike (or heal) as each of their side's turns starts; spells (Mana Coin, Spark, Healing Potion, Insight, Chain Lightning, Fireball, Quake and Meteor) do what they do and go to the discard pile. Both hands draw from the one deck, which shuffles the discard pile back in when it runs out. Change a cell and the card changes: that's the whole of balancing it.
- **Your turn**: you draw a card (a Trigger on each hand: "when `PlayerTurn` arrives, Draw"), your mana fills up, one more than last turn up to ten, and you drag the cards you can pay for onto your side of the table. A spell that hits a target is dropped straight on it (the lich, or a creature) or aimed afterwards with the dotted line. **End Turn**, **Tab** or the gamepad's **Back** hands over. The keys and a gamepad play too (left and right, Space or A, Shift or B), and a touch screen by tapping the card and then the table.
- **The lich's turn** is played by the computer, from its hidden hand along the top: it thinks, lifts the card it's going to play and aims it, plays it, and says what it did above its side of the table, until it has nothing it wants to play.
- A hero falling opens **Victory** or **Defeat** (its Health's **Death Message**), each with **Again** and **Menu**; Esc pauses.

Every card game piece is in the scene for you to change in the Inspector: the hands' **Start With**, mana and **Think Time**, the rows' **Limit** and **Discard After**, the heroes' health. It's built to mix with other games too: the **Player Hand** takes input only while it has focus, the decks, hands and rows name their actions (**Draw**, **Shuffle**, **Discard**, **Gain Mana**) and outputs (**Count**, **Mana**, **Said**), and **Victory** and **Defeat** are messages, so the duel can be a fight inside a bigger game (see [Mixing genres](/docs/mashups/)).

## Rewinds and network games

Which cards are where, in what order, which way up, the decks' and hands' state, the hands' mana, how long a card has lain on a zone with Discard After, and what effects changed (health, buffs, who played what) are all saved with the scene, so a rewind goes back to them. Effects happen where the game is run, with the game's random numbers. The computer's thinking isn't saved: after a rewind it thinks its play afresh. In a network game the host shuffles, deals and plays: a player's drags and clicks are sent to it, and only the players on a hand's side may play it (its Turn Taker's side, matched against the **Multiplayer**'s **Teams**). A card a player aims is aimed on their own machine and played by the host at what they chose; the computer's sides are played by the host, and everyone sees the card it's about to play lifted and aimed, and a creature lunge as it strikes. Nobody can peek: the cards in a face-down deck aren't sent to anyone's machine (only how many there are), and the cards in a hidden hand aren't sent to the players on other sides, whose machines draw backs in their place. A card is sent to everyone once it's played face up or discarded face up.

## Scripting

Scripts read a zone's `Cards` and `Count`, a deck's `Top`, a card's `Zone`, `IsResting`, `ShownTitle`, `ShownText`, `Cost` and `PlayedBy`, and a hand's `Picked` (the card this player has taken up), `Side`, `Mana` and `Said`. They draw with `Draw(count)`, shuffle with `Shuffle()`, play a card with `hand.Play(card, zone)` (or `hand.Play(card, zone, target)` for a card that chooses one) and discard one with `hand.Discard(card)`, and hear a card's `Drawn`, `Played` and `Discarded`, a hand's `Drew`, a zone's `CardPlayed` and a deck's `Shuffled`. A component of their own on a card's entity that is an `ICardPart` is told what happens to the card, and its `CanPlay` can keep it in the hand, for what a Card Effect doesn't do:

```csharp
// Can only be played while its player's hero is hurt, and heals by as much as the cards in the hand.
public class Desperation : Behavior, ICardPart
{
    public bool CanPlay(Card card, PlayZone zone) => Game.Find("Blue Hero")?.GetComponent<Health>() is { } h && h.Current < h.Max;

    public void Played(Card card, PlayZone zone) =>
        Game.Find("Blue Hero")?.GetComponent<Health>()?.Heal(Game.Find("Blue Hand")?.GetComponent<Hand>()?.Count ?? 0);
}
```
