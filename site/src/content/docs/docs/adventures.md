---
title: "Point-and-click adventures"
description: "Walk areas a character finds its way round, a walker that goes where you click, and hotspots to look at and use, with no code."
---

A point-and-click adventure in Griff is three components: a **Walk Area** drawn round the floor of a room, a **Walker** (the player's character) that walks wherever the floor is clicked, and **Hotspots**, the things in the room to look at and use. They're all in the Adventure section of Add Component, and the Entity menu makes each one ready to go.

## Walk areas

**Entity → Genres → Adventures → Walk Area** makes a floor 8 wide and 3 deep. Its **Points** are its corners, "x y" pairs separated by semicolons round the entity: select it and the Scene view puts a knob on each corner and a plus halfway along each side, so you drag the corners to fit the floor painted in your background, drag a plus to add a corner, and double-click a knob to take one off (it keeps three). Hold Ctrl to snap to half units. Each change is one undo step, and clicking on the floor where nothing is drawn selects it. The Scene view draws walk areas in blue; the game doesn't, unless you tick **Shown** to see where walkers can go.

Any shape with straight sides works: an L round a counter, a floor that narrows into a corridor. Several walk areas that overlap join up into one floor. Tick **Blocks** for a hole in the floor where nobody walks (a table, a pond, a well), drawn in red: walkers go round it. A hole can be part of something else: put a walk area that blocks inside a character standing in the way, and it moves with them.

**Far Scale** and **Near Scale** give a room depth: walkers are drawn at Near Scale at the walk area's bottom edge and Far Scale at its top (0.6 for 60%), shrinking evenly between, and they walk slower further back to match. The Scene view writes the two scales at the top and bottom of the area. Only how they're drawn changes: their transform and collider stay as they are.

## The walker

**Entity → Genres → Adventures → Walker** makes the player: a figure with a **Walker**, standing on a walk area (a scene with none gets one with it). Click on the floor and it walks there along the shortest way, round the holes and the corners of the floor that jut in, going straight wherever it can; click off the floor and it walks as close as it can get. Its feet are the bottom middle of its sprite (or where it is, without one): it's the feet that walk on the floor, and the drawing that shrinks with depth.

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

**Needs Flag** keeps a hotspot out of reach until a flag is set (a drawer found by reading a note) or, with `not`, once one is: a key with Use Flag `TookKey` and Needs Flag `not TookKey` can be picked up once, and give it a **Switch**'s **Hides** or a **Spawner** needing the same flag to make it vanish too. Looking and using are counted (**Times Looked**, **Times Used**: outputs for a counter or a text), and a hotspot's **Look** and **Use** actions send the walker over to do it, for a trigger or a script.

So a locked door is a hotspot with Use Line `It's locked.`, and once the key is taken (Needs Flag `TookKey` on a second hotspot over it, with Use Do **Open** on the door) it opens.

## Verbs

A left click on a hotspot does the walker's **Verb**: **Use** to begin with. **Right Click Looks** makes a right click look whatever the verb, so a game played with a mouse needs no more. For a verb bar (and for touch screens, which have no right click), make buttons whose **Action** is **Do**, with **Do** set to **Choose Look** and **Choose Use** and **Targets** the player: the next left click looks, or uses. While the verb is Look, the name by the pointer says so ("Look at Old Door"), and **Verb** is an output for a text on the bar.

## Mouse, touch and the browser

Everything is played with the mouse: the pointer's name and look, clicks, right clicks, and dialogue moved on by clicks. On a touch screen a tap is a left click, so walking, using hotspots, conversations and verb bars all work by tapping, in an exported game and in the browser (Build for Web), where the pointer changes too. A click on a button, or on a system's HUD, is the button's, not the floor's.

## Who has the clicks

A walker takes clicks while it's **Controllable**, which a **Focus** action hands over (see [Who has the player's input](/docs/players/#who-has-the-players-input)): an adventure in a corner of another game, or two characters the player switches between. **Camera** reads clicks through a camera of its own, for an adventure seen through a **Viewport**. A walker that isn't controllable (another character, or the player in a cutscene) still walks with **Walk To** (an entity's name, its hotspot's stand point if it has one, or a point, `3, -1`), stops with **Stop**, and says things with **Say**, all from triggers, buttons or scripts. A panel open over the game (a pause menu) and a conversation take the clicks while they're up.

## Rewind, saves and multiplayer

Where a walker is still to go (its **Route**), the hotspot it's walking to (**Heading**, with **Heading Verb**) and what it's saying are kept with the scene, so rewinding in the editor picks up a walk where it was and still does what it was walking over for, and the same clicks always play out the same. Flags hotspots set are kept in the save data. In a network game each player's walker answers to their own mouse and the host walks it; each player sees the names and pointer over hotspots for themselves.

## Scripts and agents

Scripts read a walker's `Feet`, `IsWalking`, `Route`, `Said`, `Hovered` and `HoveredHotspot`, call `WalkTo`, `Stop`, `Say`, `ChooseLook` and `ChooseUse`, and hear `Arrived`, `Spoke` and `Did`; a hotspot's `StandPoint`, `Called` and `CanUse`, its `Look()` and `Use()`, and its `Done` event; and a walk area's `Contains` and `ScaleAt` (`WalkArea.ScaleIn(scene, point)` for the scene's). `Input.Cursor = MouseCursor.Hand` sets the pointer from any script.

An agent builds a room with the Entity menu's tools (`Entity_CreateWalkArea`, `Entity_CreateHotspot`, `Entity_CreateWalker`) or `entity_create` and the components' fields, shapes walk areas and hotspots point by point with the Scene view's `DragPathPoint`, `InsertPathPoint` and `RemovePathPoint` (`WalkArea.2`, `Hotspot.+3`), and plays it by clicking: the play tool's `click` and `point` take a point or an entity's name (`"button": "Right"` looks), and it's told where the walker is walking to and what it says, and what the pointer is over.
