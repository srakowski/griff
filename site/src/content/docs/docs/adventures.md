---
title: "Point-and-click adventures"
description: "Walk areas a character finds its way round, a walker that goes where you click, hotspots to look at and use, and items to pick up, use on things and put together, with no code."
---

A point-and-click adventure in Griff is three components: a **Walk Area** drawn round the floor of a room, a **Walker** (the player's character) that walks wherever the floor is clicked, and **Hotspots**, the things in the room to look at and use. What the player carries is shown in an **Inventory Bar**, and a **Use With** on a hotspot says what an item used on it does (see [Items](#items)). They're all in the Adventure section of Add Component, and the Entity menu makes each one ready to go.

## Walk areas

**Entity → Genres → Adventures → Walk Area** makes a floor 8 wide and 3 deep. Its **Points** are its corners, "x y" pairs separated by semicolons round the entity: select it and the Scene view puts a knob on each corner and a plus halfway along each side, so you drag the corners to fit the floor painted in your background, drag a plus to add a corner, and double-click a knob to take one off (it keeps three). Hold Ctrl to snap to half units. Each change is one undo step, and clicking on the floor where nothing is drawn selects it. The Scene view draws walk areas in blue; the game doesn't, unless you tick **Shown** to see where walkers can go.

Any shape with straight sides works: an L round a counter, a floor that narrows into a corridor. Several walk areas that overlap join up into one floor. Tick **Blocks** for a hole in the floor where nobody walks (a table, a pond, a well), drawn in red: walkers go round it. A hole can be part of something else: put a walk area that blocks inside a character standing in the way, and it moves with them.

**Far Scale** and **Near Scale** give a room depth: walkers are drawn at Near Scale at the walk area's bottom edge and Far Scale at its top (0.6 for 60%), shrinking evenly between, and they walk slower further back to match. The Scene view writes the two scales at the top and bottom of the area. Only how they're drawn changes: their transform and collider stay as they are.

## The walker

**Entity → Genres → Adventures → Walker** makes the player: a figure with a **Walker** (and an **Inventory** with an **Inventory Bar**, see [Items](#items)), standing on a walk area (a scene with none gets one with it). Click on the floor and it walks there along the shortest way, round the holes and the corners of the floor that jut in, going straight wherever it can; click off the floor and it walks as close as it can get. Its feet are the bottom middle of its sprite (or where it is, without one): it's the feet that walk on the floor, and the drawing that shrinks with depth.

It walks at its **Speed**, faces the way it goes (**Flip Sprite** turns its sprite left and right; art should face right) and plays its **Animation**'s **Walk** and **Idle** clips if it has them, and **WalkUp**, **WalkDown**, **IdleUp** and **IdleDown** going up or down the screen (**Run** clips if it has no Walk). It ignores gravity, so it works in a game that has some.

What it says (a hotspot's line, or its **Say** action) is drawn over its head in its **Speech Color**, broken into lines and kept inside the view, for its **Say Time** (1.5 seconds) and a twentieth of a second more for each letter; a click moves on sooner. **Said** is an output, so a **Text** can show its lines somewhere else instead.

## Hotspots

**Entity → Genres → Adventures → Hotspot** makes something to look at and use: an area the size of a door with a **Hotspot**. Its shape is its **Points**, dragged in the Scene view like a walk area's (drawn in yellow, with its name) for something painted into the background; with no points it's its collider, or else its sprite, so a key with a sprite needs nothing more. Hovering over it shows its **Name** by the pointer (its entity's name if that's empty) and changes the pointer to its **Cursor** (a hand to begin with; **Look** is the arrow with a question mark, **Cross** crosshairs, and **Hidden** none).

Clicking it sends the walker to stand at its **Stand At** (from the hotspot, in world units: `0, -1.5` is just in front of a door; the Scene view draws the spot), or as close as it can get, turns it to face the hotspot and then uses it; a right click looks at it instead. Untick **Walk There** for something seen from afar, like the moon, looked at from wherever the walker stands. Each verb, Look and Use, can do any of these with no code:

- **Line**: what the walker says. `A rusty old key.`
- **Do**, **Targets** and **With**: an action, as a trigger does one (see [Triggers](/docs/story/#triggers)). A door's **Open**, a switch's **Toggle**, a dialogue's **Start**, a sound player's **Play**, a spawner's **Spawn**, a counter's **Add**: whatever the targets (or the hotspot itself, with none) can do, picked from a list.
- **Flag**: a flag set (`TookKey`), or cleared with `not TookKey`; flags are kept in the game's save data, so they last between runs (see [Dialogue flags](/docs/story/#dialogue-flags)).
- **Message**: a message sent, with the walker as its data, for a counter, a trigger or a script.

Using it can also start the **Dialogue** on its entity (tick **Use Talks**): a character to talk to, with choices, flags and parts like any other (see [Dialogue with characters](/docs/story/#dialogue-with-characters)). A click or a tap moves a conversation on, as Interact does, and picks a choice. Something with nothing to use is looked at instead, so a painting with only a Look Line answers either click.

**Needs Flag** keeps a hotspot out of reach until a flag is set (a drawer found by reading a note) or, with `not`, once one is: a key with Use Flag `TookKey` and Needs Flag `not TookKey` can be picked up once. Tick **Hide When Gone** and it vanishes from the room too while its flag keeps it out of reach (and comes back if the flag changes). Looking and using are counted (**Times Looked**, **Times Used**: outputs for a counter or a text), and a hotspot's **Look** and **Use** actions send the walker over to do it, for a trigger or a script.

So a locked door is a hotspot with Use Line `It's locked.` and a **Use With** for the key that opens it (see [Items](#items)).

## Verbs

A left click on a hotspot does the walker's **Verb**: **Use** to begin with. **Right Click Looks** makes a right click look whatever the verb, so a game played with a mouse needs no more. For a verb bar (and for touch screens, which have no right click), make buttons whose **Action** is **Do**, with **Do** set to **Choose Look** and **Choose Use** and **Targets** the player: the next left click looks, or uses. While the verb is Look, the name by the pointer says so ("Look at Old Door"), and **Verb** is an output for a text on the bar.

## Items

What the walker carries is its **Inventory** (the same one keys, pickups and counters use), shown by an **Inventory Bar** along the bottom of the screen: each item's icon with its name under it, and how many if more than one. The Walker on the Entity menu gives the player both. The bar shows up with the first item (tick **Show Empty** to have it there from the start); **Place** puts it along the top instead, **Slots** is how many show at once (arrows at its ends scroll through more), and **Bar Color** and **Text Color** set its look.

**Getting items.** A hotspot's **Use Gives** puts items in the walker's inventory when it's used (`Key`, or `Coin 3, Map`). **Entity → Genres → Adventures → Item** makes something to take: a small sprite named Key whose hotspot gives a Key once (Use Flag `TookKey`, Needs Flag `not TookKey`, Hide When Gone), so it's gone from the room once taken, for good. Rename it, and give it a picture: an item with no icon of its own is drawn with the picture of the hotspot that gives it. A **Pickup** with an **Item**, a combination, a **Use With**'s **Gives** and scripts (`Add("Key")`) fill it too. With the inventory's **Save As** (`Items`, as the Walker on the Entity menu sets it) it's kept in the save data: still there the next time the game runs, and carried into the next room when that scene's walker has an inventory saved as the same name (**Game → Clear Save Data** forgets it while you test).

**Picking one.** Click an item on the bar and it's picked up: the pointer turns into the item, and over a hotspot the name by it says what it'd do (`Use Key with Old Door`). Click the hotspot and the walker walks over and uses the item there; click the item again, or right-click anywhere, to put it back. Dragging an item off the bar and letting go over a hotspot does the same in one go. Clicking the floor with an item picked walks there, still holding it.

**Using it on a hotspot.** Add a **Use With** to the hotspot for each item that does something there. Its **Item** is the item (`Key`), and when it's used there, with no code:

- **Line**: what the walker says. `The key turns. It's open!`
- **Do**, **Targets** and **With**: an action on the targets (or the hotspot itself), like a hotspot's: a door's **Open**, a switch's **Toggle**.
- **Flag**: a flag set (`DoorOpen`), or cleared with `not DoorOpen`.
- **Message**: a message sent, with the walker as its data.
- **Talks**: starts the dialogue on the hotspot's entity, for giving something to a character.
- **Keeps Item**: keeps the item (a key that opens more than one door, a torch to light things with); otherwise it's given up.
- **Gives**: items the walker gets in return (`Coin 2`, `Map`).
- **Sound**: a sound played.

**Needs Flag** makes a Use With count only while a flag is set (or isn't), so a candle can take a torch once to be lit (`not Lit`) and say something else after (`Lit`): the first whose flag holds is the one done. An item a hotspot has no Use With for gets its **Wrong Item Line** (`The dog sniffs it and looks away.`), or else the bar's **Wrong Line** (`That doesn't work.`), the same for every hotspot, and the walker keeps it. **Times Used** counts each Use With, and the walker's `UsedItem` event says what was used where and whether it worked.

**Putting two together.** Pick one item and click another on the bar (or drag one onto the other). The bar's **Combinations** say which go together, one per row: what they make, `=`, the two joined with `+`, and after a colon what the walker says: `Grappling Hook = Rope + Hook: I tie the rope to the hook.` Either way round works; both are used up and what they make goes in. To keep one, make it again: `Knife + Short Rope = Knife + Rope`. Two that don't go together get the Wrong Line, and both are kept.

**Looking at one.** Right-click an item (or click it with the Look verb) and the walker says its line from **Descriptions**, one per row: `Key: A rusty old key.` An item with none is just named.

**Icons** are one per row too: `Key: Sprites/key.png`, or a frame of a sprite sheet with its columns and rows and the frame, `Rope: Sprites/items.png 4x2 5`. An item with no icon is drawn with the picture of the hotspot in the scene that gives it (its Use Gives), or else as a tile with its first letter, in that hotspot's sprite color or a color of its own.

**From a table.** For a game with many items, name a data table as the bar's **Table**: a row for each item, named like it, with a **Description** and an **Icon** column. **Combinations Table** names one of combinations: a row for each, named like what it makes, with a **Combine** column (`Rope + Hook`) and a **Line** column. The bar's own rows win over the tables'.

**Sounds**: **Get Sound** as an item comes in, **Pick Sound** as one is picked up, and **Combine Sound** as two go together.

## The keys and a controller

The whole game plays without a mouse too. Left and right (the arrows, A and D, the d-pad or the stick) go from one hotspot in view to the next, a marker over the one they're on with its name; the walker's **Use Action** (Jump: Space or the gamepad's A) uses it (with the item picked, if there is one) and its **Look Action** (Attack: X or the gamepad's X) looks at it. Its **Items Action** (Interact: E, Enter or the gamepad's Y) goes to the bar, where left and right go from item to item: Use picks one up and goes back to the room to use it, or puts the one picked together with this one; Look looks at it; and Items goes back. Its **Cancel Action** (Dash: Shift or the gamepad's B) puts back the item picked, or leaves the bar. The first press shows where the keys are, and moving the mouse hands back to it. Each is an input action, so the Controls window (and players) can rebind them.

## Mouse, touch and the browser

Everything is played with the mouse: the pointer's name and look, clicks, right clicks, dialogue moved on by clicks, and items picked, dragged and used. On a touch screen a tap is a left click, so walking, using hotspots, conversations, verb bars and items all work by tapping (tap an item and then what to use it on, or drag it there; tap it again to put it back), in an exported game and in the browser (Build for Web), where the pointer changes too. A click on a button, on the inventory bar, or on a system's HUD, is theirs, not the floor's.

## Who has the clicks

A walker takes clicks and keys while it's **Controllable**, which a **Focus** action hands over (see [Who has the player's input](/docs/players/#who-has-the-players-input)): an adventure in a corner of another game, or two characters the player switches between. **Camera** reads clicks through a camera of its own, for an adventure seen through a **Viewport** (its inventory bar goes in that view). A walker that isn't controllable (another character, or the player in a cutscene) still walks with **Walk To** (an entity's name, its hotspot's stand point if it has one, or a point, `3, -1`), stops with **Stop**, and says things with **Say**, all from triggers, buttons or scripts; a hotspot's **Use Item** action (`Key`) sends the player's walker over to use an item there, and the bar's **Combine** (`Rope + Hook`) and **Look At** do what clicks on it do. A panel open over the game (a pause menu) and a conversation take the clicks while they're up.

## Rewind, saves and multiplayer

Where a walker is still to go (its **Route**), the hotspot it's walking to (**Heading**, with **Heading Verb** and the **Heading Item** it's going to use there), what it's saying, what it carries, the item picked from the bar (**Holding**) and where the keys are (**Pointed**, and the bar's **Cursor** and **Scroll**) are kept with the scene, so rewinding in the editor picks up a walk where it was and still does what it was walking over for, and the same clicks and keys always play out the same. Flags hotspots and Use Withs set are kept in the save data, and so is an inventory with a **Save As**. In a network game each player's walker answers to their own mouse and keys and the host walks it; each player has their own inventory and bar, and sees the names and pointer over hotspots for themselves.

## Scripts and agents

Scripts read a walker's `Feet`, `IsWalking`, `Route`, `Said`, `Hovered`, `HoveredHotspot` and `HoveredItem`, call `WalkTo`, `Stop`, `Say`, `ChooseLook` and `ChooseUse`, and hear `Arrived`, `Spoke`, `Did` and `UsedItem`; a hotspot's `StandPoint`, `Called` and `CanUse`, its `Look()`, `Use()` and `UseItem("Key")`, and its `Done` event; an inventory bar's `Holding`, its `Pick`, `PutBack`, `Combine("Rope + Hook")` and `LookAt`, its `DescriptionOf`, `IconOf` and `CombinationOf`, and its `Picked`, `Combined` and `Looked` events; a Use With's `Takes(item)` and `Done` event; and a walk area's `Contains` and `ScaleAt` (`WalkArea.ScaleIn(scene, point)` for the scene's). `Input.Cursor = MouseCursor.Hand` sets the pointer from any script.

An agent builds a room with the Entity menu's tools (`Entity_CreateWalkArea`, `Entity_CreateHotspot`, `Entity_CreateWalker`, `Entity_CreateItem`) or `entity_create` and the components' fields (a Use With is a component like any other: `UseWith#2` is a hotspot's second), shapes walk areas and hotspots point by point with the Scene view's `DragPathPoint`, `InsertPathPoint` and `RemovePathPoint` (`WalkArea.2`, `Hotspot.+3`), and plays it: the play tool's `click`, `point` and `drag` take a point, an entity's name or an item on the bar by its label (`"Item: Key"`; the report lists what's on the screen), `"button": "Right"` looks, and `tap` presses the keys. It's told where the walker is walking to and what for, what it says and carries, the item picked, where the keys are, and what the pointer is over.
