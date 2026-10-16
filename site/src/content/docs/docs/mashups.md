---
title: "Mixing genres"
description: "Genres sharing one scene: who has the input, what works on part of the scene, pausing part of it, viewports, genres driving each other, what each genre offers, and the teams, items and grid every genre shares."
---

A jam themed "combine two genres" wants a match-3 board inside a platformer, a strategy map in one corner of a shooter, or a Sokoban room in an RPG. Griff's genre pieces share a scene without code: each takes input only while it has focus, works on its own part of the scene, pauses on its own, and can be seen through a camera of its own, while triggers and outputs let them drive and read each other, and they share one idea of a team, of items and of a grid.

## Who has the input

Everything that reads the player's keys, mouse or gamepad has a **Controllable** tick and a **Focus** action (see [Who has the player's input](/docs/players/#who-has-the-players-input)): the movements, match-3 and falling-block boards, the strategy **Commander**, the **Tactics Controls**, a card game's **Hand**, an adventure's **Walker**, a visual novel's **Stage**, a rhythm game's **Note Lanes**, a fighter, pinball's flippers, plunger and tilt, a **Slingshot**, the **Sandbox Tools**, a **Block Builder** and an RPG **Battler** (the table below lists them). What has none of its own goes by another's: a first-person **Weapon** fires while its player's movement has the input, and a **Move History** hears Undo while a grid player it covers has it. Focus gives the input to one entity (and what's inside it) and takes it from everything else the same player plays, so a board and a platformer's player don't both move on the arrow keys. A trigger hands it over: on an arcade cabinet, "when `Play` arrives, Focus the Board", and on the board, "when `Won` arrives, Focus the Player". Scripts call `Game.Focus(entity)` and ask `Game.HasFocus(entity)`.

## Working on part of the scene

What used to cover the whole scene can cover part of it, named by its **Within**: an entity's name, covering whatever is inside that entity (a group) and whatever stands in its area (a Room's, a tilemap's, a collider's, or a strategy map's). Empty still covers the whole scene.

- A **Move History** within a puzzle room counts and takes back only the steps there, and hears Undo while a grid player with the input stands there (see [Undo and restart](/docs/puzzles/#undo-and-restart)).
- A **Turn Order** within an arena runs the turns of only the turn takers there; something that walks in joins them (see [Turns](/docs/turns/#turns)). Several can run side by side.
- **Sandbox Tools** within a workshop grab, join, wire and remove only what's there, and spawn only there.
- A strategy **Commander**'s game is played on its map: units and buildings on it are its game's, its fog covers only it, and its HUD shows only in its own camera's view. Several commanders play games of their own (see [Real-time strategy](/docs/genres/#real-time-strategy)).
- A script's own component can do the same with `Scope.Of(Entity, Within)` and its `Covers(entity)`, and give its entity an area by marking a `Box` property `[Area]`.

## Pausing part of the game

A **Pause Group** pauses one group (the minigame, the minions while the boss talks) while the rest of the game runs on, and a panel's or a dialogue's **Pauses** pauses just that group while it's open (see [Pausing part of the game](/docs/ui/#pausing-part-of-the-game)).

## Seeing two things at once

A camera with a **Viewport** draws into part of the screen: a split screen, a minimap, or a first-person window in a 2D game (see [Viewports](/docs/players/#viewports)). A strategy Commander on a viewport's camera plays there, the mouse read through that view.

## Genres driving each other

Components name what they do and what they know, so any genre's pieces drive and read any other's with no code: a **Trigger** does any action when a message arrives ("when `Matched` arrives from the Board, Fire the Cannon"; see [Triggers](/docs/story/#triggers)), a **Message Scope** keeps two boards' messages apart (see [Messages to part of the scene](/docs/story/#messages-to-part-of-the-scene)), and a Counter, a Bar, a Text or a wire shows or carries any output, such as a board's Score or a faction's Gold (see [Outputs](/docs/story/#outputs)). A card's **Card Effect** can **Do** any action too, so playing a card can open a door in a platformer level, start a spawner or damage the boss (see [Card effects](/docs/cards/#card-effects)), and a hand's **Gain Mana** action lets the rest of the game pay for the cards: a coin picked up, a gem matched. A rhythm game's **Note Lanes** send **Hit** (with the points), **Perfect** and **Miss** as each note is judged, so a note on the beat can fire a weapon, a Perfect heal the hero and a Miss let a monster through (see [Rhythm games](/docs/rhythm/#messages-a-note-can-fire-a-weapon)).

### Four mixes, no code

- **A card spawns an enemy.** Give the card a **Card Effect** whose **Do** is `Spawn` and **Do On** is the platformer's spawner (`Slime Pit`, with **Running** unticked so it spawns only when told). Playing the card spawns a slime there.
- **A rhythm note fires a weapon.** On the hero, a **Trigger** hearing `Hit` **From** the `Lanes` does **Fire**: a first-person **Weapon** (the one in hand, ready and with ammo, whether or not the hero has the input) or a **Shooter**. Each note hit on the beat is a shot; a missed one isn't.
- **A pinball bumper hurts a boss.** On the boss, a **Trigger** hearing the bumper's **Message** (`Score`) **From** the `Bumper` does **Damage** **With** `2`. Each time the ball hits that bumper, the boss loses 2.
- **A hotspot opens any door.** On an adventure **Hotspot** (a lever), **Use Do** is `Open` and **Use Targets** the door (`Vault Door`, a locked **Door** needing a key anywhere in the game). The walker walks over, pulls the lever, and the door is open for good, key or not. Its **Use Flag** works too, for a door that **Needs Flag**.

## What each genre offers

Every genre names what it does (its actions, for a **Trigger**, a card's **Do**, a hotspot's **Use Do** or a wire) and what it knows (its outputs, for a **Counter**, **Bar**, **Text** or wire), and what of it reads the player's input takes it only with focus. Griff's own tests hold each genre to that, so a new one can't leave it out. Every piece in the input column also has a **Focus** action.

| Genre | Takes the input | Does (actions) | Knows (outputs) | Sends (messages) |
| --- | --- | --- | --- | --- |
| [Match-3](/docs/puzzles/#match-3-boards) | Match Board | Shuffle, Pause, Resume, Restart | Score, Swaps, Moves Left, Chain | Match, Score, Level Complete, Out of Moves |
| [Falling blocks](/docs/puzzles/#falling-blocks) | Falling Board | Pause, Resume, Restart | Score, Lines, Level, Game Over | Lines, Score, Level Up, Game Over |
| [Sokoban puzzles](/docs/puzzles/#sokoban-crates-and-goals) | the grid player (a Move History goes by it) | Move History: Undo, Restart | Goal: Covered, Goals Covered; Move History: Moves | a goal's Covered, All Covered; Undo, Restart |
| [Roguelike dungeons](/docs/turns/#dungeons) | the grid player | Dungeon Generator: Next Floor | Floor | Floor |
| [Real-time strategy](/docs/genres/#real-time-strategy) | Commander | Producer: Train, Cancel | Faction: Amount (`Amount:Gold`), Units, Buildings; Producer: Queued, Progress | Defeated |
| [Tactics](/docs/turns/#tactics) | Tactics Controls | Tactics Unit: Wait, Undo Move | Tactics Unit: Moved, Can Act, Done | the turn order's |
| [First person](/docs/genres/#first-person-shooters) | First Person Movement (its Weapon goes by it) | Weapon: Fire | Weapon: Shots, Cooling Down | |
| [Block worlds](/docs/genres/#block-worlds) | Block Builder (its Hotbar and Crafting go by it) | Hotbar: Next, Previous, Pick | Hotbar: Selected Item, Selected Count, Blocks (`Blocks:Dirt`) | |
| [Contraptions](/docs/genres/#contraptions-and-wiring) | Sandbox Tools | Wire Button: Press, and a wire into any action | Wire Button: On; Wire Gate: Out; Wire Sensor: Out, Count | |
| [Cards](/docs/cards/) | Hand | Hand: Draw, Discard, Gain Mana; Deck: Draw, Shuffle, Restart; Play Zone: Discard; a Card Effect's Do: any action | Hand: Count, Mana, Said; Deck: Count, Shuffles; Play Zone: Count, Played; Card: Value, Cost | a play zone's Play, a hand's Draw, a deck's Shuffle and Empty |
| [Adventures](/docs/adventures/) | Walker | Walker: Walk To, Stop, Say, Choose Look, Choose Use; Hotspot: Look, Use, Use Item; Inventory Bar: Pick, Put Back, Combine, Look At; a hotspot's Look Do and Use Do: any action | Walker: Said, Hovered, Verb; Hotspot: Name, Times Looked, Times Used; Inventory Bar: Holding | a hotspot's Look and Use messages |
| [Visual novels](/docs/visual-novels/) | Stage | Stage: Background, Show, Hide, Hide All, Direct; Character: Show, Hide, Look, Stand | Stage: Background, Speaking, On Stage; Character: Expression, Position, On Stage | a line's `[send]` |
| [Rhythm](/docs/rhythm/) | Note Lanes, Song Select | Note Lanes: Start, Play, Stop; Song Select: Next, Previous, Next Difficulty, Previous Difficulty, Play, Pick | Note Lanes: Score, Combo, Best Combo, Accuracy, Grade, Judgment, Progress...; Song Select: Title, Difficulty, Best, Best Grade | Hit (with the points), Perfect, Miss, Song Complete |
| [Fighting](/docs/fighting-games/) | Fighting Character, Character Select | Fighting Character: Perform, Reset Combo, Next Auto Block; Hitboxes: Stun, Recover; Versus Match: Rematch, Reset Positions, Toggle Hitboxes; Computer Fighter: Next Behavior | Fighting Character: Move, Combo, Combo Damage, Blocking...; Hitboxes: Hits, Hit, Got Hit, Stunned; Versus Match: Round, First Wins, Second Wins, Time Left, Winner | Hit, Got Hit, Block, Round, Round Over, Match Over |
| [Physics puzzlers](/docs/physics-games/) | Slingshot | Slingshot: Load, Launch; Breakable: Break, Damage, Repair; Level Goal: Win, Lose | Slingshot: Shots Left, Out Of Shots, Stretch; Breakable: Damage, Broken; Level Goal: Stars, Targets Left, Won, Lost, Best Score | Break, Damage, Level Complete, Level Failed, Out of Shots |
| [Pinball](/docs/physics-games/#pinball) | Flipper, Plunger, Tilt | Flipper: Flip, Raise, Lower; Plunger: Launch; Bumper: Flash; Target and Lane: Light, Reset; Drain: Extra Ball, Save Ball; Tilt: Nudge, Tilt, Reset | Bumper: Hits, Lit; Target: Lit, Down, Hits; Lane: Lit, Passes; Drain: Ball, Balls Left, Game Over; Plunger: Pull; Tilt: Danger, Tilted | Score (with the points), the drain's, the plunger's, the tilt's |
| [RPG battles](/docs/turns/#rpg-battles) | Battler | Battler: Attack, Cast Spell, Use Item | Battler: Mana, Spell, Spell Cost, Item, Items, Title, Said | |

The platformer's and top-down game's own pieces (movements, Health, Spawner, Shooter, Door, Switch, Counter) have theirs too (see [Triggers](/docs/story/#triggers) and [Outputs](/docs/story/#outputs)), and `component_describe` lists every component's for agents. A script's own component joins in the same way: `[Action]` on a method, `[Output]` on a property, and `[Focus]` on its Controllable or `[FocusFrom(typeof(...))]` on the class (see [Scripting](/docs/scripting/)).

## Teams

Every genre's teams are one idea: a name, `Blue` or `Player`, given by a turn order's **Teams**, a strategy **Faction**, a network game's Multiplayer **Teams** or a **Team** component, and carried by anything on that team: a **Turn Taker**'s **Team**, a strategy **Unit**'s or **Building**'s, a fighter's **Hitboxes**' **Team**, or a **Team** on anything else (**Entity → Add Component → Team**). An entity is on the first team named on it, or on the entity it's inside (a sword's hitbox is on its wielder's team); in a network game with teams, a player's character is on its player's. Names ignore case, and every field that names a team picks from all of them, flagged when no one gives that name.

Things on the same team are allies and things on different teams are foes, in every genre at once:

- Allies don't hurt each other: a **Damage On Touch**, a **Bump Attack**, a fighter's hitboxes and a first-person weapon pass over their own team (in a network game, unless the Multiplayer has **Friendly Fire**). A shot is on its shooter's team.
- A turn order's teams take turns, so a platformer hero with a **Team** of `Red` (and a Turn Taker with its Team left empty) moves on Red's turn.
- A tactics unit's foes are the units of other teams and anything else with health on another team; a strategy unit fights units and buildings of other teams and anything else with health on another team on its map; a card effect's friends are its hand's team; a **Battler** with no **Foe** named fights the first battler standing on another team; a fighter's opponent is the nearest fighter not on its team.
- A team's items are shared (see below).

Scenes saved when a turn order had **Sides**, a turn taker a **Side**, and fighters their own hitbox teams still load: the values move across to the new names. Scripts ask `Teams.Of(entity)`, `Teams.Allies(a, b)` and `Teams.Foes(a, b)`, and `Game.TeamOf(entity)` (the network player's team in a network game); a script's own component joins in by implementing `ITeamMember`.

## Items and resources

Every genre's items and resources are one idea too: a name and how many, `Key`, `Gold 250`, `Dirt 12`, `Mana 3`, held by whatever holds items: an **Inventory** (a hero's, a block world's player's), a strategy **Faction**'s **Resources** (a row each, as an inventory's items), or a card **Hand**'s mana (the item `Mana`). Lists of them read the same everywhere, `Coin 3, Map` or `150 Gold, 50 Wood`, the count before or after the name, and every field that names an item picks from the same list: what pickups give, inventories hold, resources are called and recipes make.

Something collects into and pays from the items on it or the entity it's inside, else its team's: an **Inventory** whose **For Team** is its team, or its team's strategy faction. So:

- A platformer hero on the `Player` team with no inventory of its own collects coins (a **Pickup** with an **Item**) into the Player faction's gold, and the faction trains soldiers with them.
- A **Pickup**'s **Price** (`5 Coin`) makes it a shop's ware: it stays until whoever touches it can pay, and takes the price as it's collected.
- A card's **Price** (`2 Gold`) is what it costs beside its mana, paid from its hand's team's items; a card that can't be paid for is dimmed and can't be played. `Mana` a teammate picks up goes into the hand.
- An inventory's **Add Items** and **Take Items** actions (`Gold 50`) let a trigger pay out a prize or charge a toll: "when `Won` arrives from the match-3 board, Add Items `Gold 50`".

A strategy unit's or building's **Cost** is paid from its team's resources as before. Scenes saved when a faction's resources were one line of text (`Gold 250, Wood 100`) still load, a row each. Scripts use `Game.CountItems(entity, "Gold")`, `Game.CanPay(entity, "150 Gold")`, `Game.Pay(entity, "150 Gold")` and `Game.Give(entity, "Key", 1)`, and `Items.Parse` reads a list; a script's own component holds items by implementing `IHoldsItems`.

## One grid

A match-3 or falling-blocks board keeps its pieces in a tilemap of its own, and that tilemap can line up with a level's: set its **Lines Up With** to the level's tilemap (`Level`), and it takes the level's tile size and shape and keeps its top left on a corner of the level's cells (moved to the nearest as it's set, dragged in the Scene view, or while the game runs). A board made with **Create Falling Board** or **Create Match Board** in a scene with a square tilemap lines up with it by itself. Tick the board's tilemap's **Solid** and its settled pieces are walls and floors like the level's tiles: a platformer hero climbs a falling-blocks stack on the level's squares, and falls when a row clears under it (the piece still falling isn't solid).
