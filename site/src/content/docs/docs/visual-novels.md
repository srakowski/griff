---
title: "Visual novels"
description: "A stage with backgrounds and characters at named positions, with expressions, directed by the lines of a dialogue: show, hide and move them, change the background, cut, fade or slide; save slots, a log of lines read, skip and auto, with no code; and the Visual Novel template, a whole short story with three endings."
---

## The Visual Novel template

**File → New Project** and the **Visual Novel** template make a whole short visual novel with no code, ready to rewrite: *Starfall Café*. At closing time a little star, Comet, falls into the café's flour; June, who runs the café, and Felix, who saw it fall past his telescope, have until sunrise to get it home. How June warms Comet up (a mug of cocoa or the bread oven) sets a flag, and on the roof she sends it home or asks it to stay: three endings, two of them told apart by the flag.

- The **Main** scene is the title screen: **New Game**, **Continue** (the game saved last; faded until there is one), **Load** (the load screen's six slots), **Settings** and **Quit**. Click or tap, or use the arrows (or D-pad) and Enter (or A).
- The **Story** scene is the story: a **Stage** with three backgrounds (the café and the roof are pictures, the dawn two colors running top to bottom), cut, faded and slid between, and June, Felix and Comet on it. June and Felix have four faces each, pictures; Comet's five faces are clips of one sprite sheet, so it twinkles as it talks, and it floats up to the stage's own **Sky** position to go home. The whole story is the stage's **Dialogue**: open it in the Dialogue Editor (**Entity → Edit Dialogue...**) to read it line by line, or its **Lines** in the Inspector. New Game and Play Again are buttons whose **Action** is **NewGame**: they forget the flags (the progress, not the saved games, the lines read or the settings), so each game starts fresh.
- Reading: a click or tap, Space or the gamepad's A reads on; the arrows or D-pad and Enter (or Y), a click on one, or its number key pick a choice. The [reading menus](#the-reading-menus) are along the top (Log, Skip, Auto, Q.Save, Q.Load, Save, Load, Settings), with H, Ctrl and A for the log, skipping and Auto. Esc or Start opens a pause menu with Save, Load, Settings and the title screen, so the keys and a controller reach everything.
- Each ending opens a panel of its own (the story sends its name as a message, `[send HomeEnding]`, and the panel's **Open Message** is that) with **Play Again** and **Title**. Sound players play the crash, the café's bell and Comet's glow coming back on their messages (`[send Crash]`), the lines tick as they type, and the music is made in code like the pictures (in **Assets/Story**, to repaint in the Sprite Painter).

To make it your own, rewrite the lines, swap the pictures in the stage's **Backgrounds** and the characters' **Expressions**, and add a character with **Entity → Genres → Visual Novels → Character**.

## A stage

**Entity → Genres → Visual Novels → Stage** makes a visual novel scene you can play straight away: a **Stage** filling the camera's view with a park, a room and a night sky to show, Ava and Ben inside it (off stage until the script brings them on), each with three expressions, a dialogue box along the bottom whose lines type themselves out and wrap, a short script on the stage that sets the scene, brings them on, changes their faces, offers a choice and moves home, and the [reading menus](#the-reading-menus): Log, Skip, Auto, quick saves, save and load screens and settings. Play, and the script starts by itself. Its pictures go in **Assets/Stage**, to repaint in the Sprite Painter. **Entity → Genres → Visual Novels → Character** adds someone else, inside the stage, at a free position.

A **Stage** draws a background filling its **Size** (as much as the camera shows, for a whole screen), under everything else (its **Order**). Its **Backgrounds** are one per row, a name and what it looks like: a picture (`Park: Stage/park.png`), a color (`Night: #10183A`) or a color at the top running to another at the bottom (`Dusk: #F2A65A #3A2F5B`). **Background** is the one showing: set it in the Inspector for the one the game starts with. Its **Positions** are where characters stand, one per row, a one-word name and how far across from the stage's middle (and, after a comma, how far up): `Left: -4.5`, `Center: 0` and `Right: 4.5` to begin with, and any of your own (`FarLeft: -7, -0.5`). The Inspector edits both as rows (a name and how far across and up; a name and a picture or two colors), and a character's **Expression** and **Position**, and the stage's **Background**, are picked from them.

## Characters and expressions

A **Character** is someone who appears on the stage: an entity inside it with a **Sprite** (how they look) and usually a **Speaker** (their name, which their lines start with). They're on stage while their entity is shown, standing at their **Position**, one of the stage's: pick it in the Inspector and they stand there. Their **Expressions** are one per row, a one-word name and either a picture for their sprite (`Happy: Stage/ava-happy.png`) or the name of a clip their **Animation** plays (`Laugh: Laugh`, a laugh that moves, or a blink). **Expression** is how they look now: set it and their sprite shows it. With no code, a character's own **Show**, **Hide**, **Look** (an expression) and **Stand** (a position) actions bring them on and off and change them from a trigger, a card or a hotspot, and their **Expression**, **Position** and **On Stage** outputs show how they are.

## Directing from the lines

The stage's **Dialogue** directs it: tags at the start of a line are done as it's said, alongside the flags and messages of [Dialogue flags](/docs/story/#dialogue-flags). A dialogue directs the stage it's inside, or else the scene's first.

- `[show Ava left happy]` brings Ava on at the left, looking happy. The words after her name can come in any order, with commas and little words between (`[show Ava at left, happy]`), and any can be left out: `[show Ava]` brings her on where she stood last (or in the middle). On stage already, it moves her and changes her face.
- `[move Ava right]` walks her to another position over the stage's **Move Time**.
- `[hide Ava]` takes her off; `[hide all]` clears the stage.
- `[background Park]` (or `[bg Park]`) changes the background.
- A tag can end with how it happens: `cut` (at once), `fade` or `slide` (over the stage's **Transition Time**). Characters slide in from the side of the stage their position is on, and out that way; a background slides in over the last from the right. Without one, the stage's **Background Transition** and **Character Transition** say (Fade to begin with).
- `Ava (sad): Oh.` says a line looking sad: the speaker's expression changes as the line is said, without a tag.

A row of tags alone just does them and goes on, so a scene can be set before anyone speaks:

```
[background Park fade]
[show Ava left happy] [show Ben right slide]
Ava: What a lovely day!
Ben (surprised): Ava? I didn't expect to see you here!
[move Ben center] Ben (happy): Don't mind if I do.
> Stay in the sun -> Park
> Go home for tea -> Home
# Home
[background Room fade] [hide Ben] Ava: Home at last.
```

Whoever speaks is lit and the others on stage are dimmed (by the stage's **Dim Others**), so you can see who's talking; when nobody on stage speaks (a narrator), nobody is. A word the stage doesn't know (a position, an expression or a background it hasn't got, or a character who isn't on it) is said in Output as the line is said, naming what it does have, and the rest of the tag is done.

## Reading

Lines type themselves out (the box's **Typewriter**), and long ones wrap (the **Dialogue Box**'s **Wrap Width**). A click or a tap anywhere (off buttons), Interact (E, Enter, the gamepad's Y), or Space or the gamepad's A (the stage's **Advance Action**, Jump to begin with) reads on: the first press finishes the line being typed, the next shows the next line. With choices showing, a click on one picks it, as do the arrow keys or D-pad and Interact, or a choice's number key; a click off them doesn't. A dialogue that starts with the scene (its **Start** set to **Scene Start**, as a new stage's is) needs no collider, and once said isn't said again until the scene is loaded again; set **Pause Game** off on it, as a new stage's is, so expressions that are clips keep moving. The stage plays on in real time either way, and holds still under a pause menu or in a paused group.

## Skip and Auto

Every line shown is remembered as read, and kept in the game's save data, so it's still read the next time the game runs (in a browser, in its storage). A line changed since reads as new.

- **Skip** goes through the lines read before, a line every twentieth of a second, with fades, slides and walks done at once, and stops at choices and at the first line not read yet. Hold Ctrl or the gamepad's right trigger (the stage's **Skip Action**, `Skip`) to skip while it's held, or click the Skip button to skip until it stops; reading on by hand (a click, a tap, Interact) stops it too. With the player's **Skip Unread** setting on, it goes through lines not read yet as well, and still stops at choices.
- **Auto** reads on by itself: once a line has typed out, the next comes after a pause as long as the line (a short "Hi." moves on in a second, a long line waits a few), shorter or longer as the player's **Auto Speed** setting says. It waits at choices, and stays on from one talk to the next. A (or the gamepad's X; the stage's **Auto Action**, `Auto`) turns it on and off, as does the Auto button.

The two settings are the player's, kept by the Visual Novels module in the save data like the volumes (with the lines read): a **Slider** set to **Auto Speed** and a **Toggle** set to **Skip Unread** change them, and a toggle set to **Skip** or **Auto** is a button that shows and flips skipping or reading on by itself. Scripts use `Game.Skipping`, `Game.AutoReading`, `Game.SkipUnread`, `Game.AutoSpeed`, `Game.AutoDelay(line)` and `Game.HasRead(dialogue, row)`. Skip and Auto work for any dialogue, not only a stage's.

## The log

A **Backlog** keeps the lines said so far, each with who said it (a narrator's alone) and the choices picked, newest at the bottom: every dialogue's lines go in it as they're said, up to its **Keep** (200). Put it on a panel and it's a log to read back: the mouse wheel, the arrow keys, the D-pad or stick, Page Up and Page Down, Home and End scroll it, and so does dragging it (a finger on a touch screen), with a bar down its side to show where you are. With its **Open On Wheel**, turning the wheel up over the game opens its panel, and turning it down past the newest line closes it again. The reading menus' log opens with H or the gamepad's left shoulder too (its panel's **Open On** is the `Log` action), and closes with the same, Esc or B. Its **Lines** are saved with the scene, so a saved game or a rewind has the lines said before it.

## Saving and loading

A game is saved in a slot of its own name ("1", "2"... "Quick" for quick saves) with everything a rewind keeps: the scene as it is, so the stage (the background, who's on, where and how they look), where the talk has got to and the log come back exactly, and the flags set then, which loading puts back as they were (clearing those set since). The panels open over the game (the save screen it's saved from) are closed in it. A screenshot of the game without them is taken as it's next drawn, and kept with it, with when it was saved and the line being said. Saved games are kept in the save data, so they're there the next time the game runs, on the desktop and in a browser alike.

A **Save Slot** shows a slot on a save or load screen: its screenshot, and what its **Format** says (`{date}  {time}` and `{line}` to begin with; `{slot}` and `{scene}` too), or its **Empty Text**. Put it on a **Button** whose **Action** is **Save Game**, **Load Game** or **Delete Save**: such a button with no **Slot** of its own uses the save slot it's on or inside, so a delete X inside a card deletes the card's. **Quick Save** and **Quick Load** buttons use the Quick slot, and a **Continue** button (a title screen's) loads the game saved last, in any slot. Load Game, Delete Save, Quick Load and Continue buttons are faded while there's nothing to load; a Load Game, Quick Load or Continue button's **Fade** fades out and back in around it. Scripts call `Game.SaveGame(slot)`, `LoadGame(slot)`, `DeleteSave(slot)`, `QuickSave()`, `QuickLoad()` and `ContinueGame()`, and read `SavedGames`, `SavedGameIn(slot)` and `LatestSave` (each with its `Saved` time, `SceneName`, `Line` and `Describe(format)`), with `GameSaved` and `GameLoaded` events. A network game can't be saved or loaded: it isn't one machine's alone.

## The reading menus

**Entity → Genres → Visual Novels → Reading Menus** (and every new Stage) makes them, with no code:

- A row of buttons at the top of the screen: **Log**, **Skip** and **Auto** (toggles that show when they're on), **Q.Save** and **Q.Load**, and **Save**, **Load** and **Settings**, which open their screens. They aren't picked with the arrow keys, which are for the choices.
- **Save Screen** and **Load Screen**: six slots each, a card with the screenshot, when it was saved and the line, and an X in its corner to delete it. Click or tap a card, or pick it with the arrow keys or D-pad and press Enter or A; Esc, B or Back closes the screen.
- **Log Screen**: the lines said, as above.
- **Reading Settings**: Auto speed, Skip (lines read before, or all lines) and the volume.

They're ordinary panels, buttons, sliders and toggles, so restyle, move or rename them in the Inspector.

## The Dialogue Editor

**Entity → Edit Dialogue...** opens the Dialogue Editor on the selected entity's dialogue (or that of the stage a selected character is on). It lists the lines, each with who says it and what its tags do ("Show Ava at Left, Happy"), and for the line picked:

- **Said by** and **looking**: the speaker, picked from everyone with a Speaker in the scene, and the expression they say it with, from theirs.
- **Says**: what's said, and **As written**: the whole line, tags and all, to type.
- **Goes to**, for a choice or a go-to: where it goes, picked from the dialogue's parts and End (a choice can also go on after the choices).
- **Flags and messages**: the line's, in words ("Only while the flag MetKing is set", "Sets the flag MetKing", "Sends the message Greeted"), each with a button to remove it, and below them one to add: if, if not, set, clear or send, with the flag picked from those the project sets or the message from those it sends and hears (or typed, for a new one). Any dialogue has these, a stage or not.
- **Stage directions**: the line's, each with a button to remove it, and below them a direction to add, built from lists of what the stage has: Show, Hide, Move or Background, the character, the position, the expression, the background and cut, fade or slide.
- **Stage at this line**: a preview of the stage as the lines above leave it (read top to bottom, whichever way the choices go), the speaker lit and the others dimmed, with the line in the box, and the same in words underneath. Directions that name something the stage doesn't have are flagged above it.

Lines, choices, parts and go-tos are added (**Add Line**, **Add Choice**, **Add Part**, **Add Go To**), removed and moved up and down from there too. Every change rewrites the dialogue's **Lines**, each one undo step, so it's the same as typing them in the Inspector. It follows the selection, so it can stay open beside the scene. Agents do the same through the MCP: the `Story.EditDialogue` tool opens it, and `ui_invoke` and `ui_set` on **DialogueEditor** pick a line (`SelectLine`), set **Speaker**, **Expression** and **Said**, add a direction (`AddDirection` with `show`, `Ava`, `Left`, `Happy`), add a flag or message (`AddTag` with `set` and `MetKing`), set where a choice goes (**GoesTo**), add choices, parts and go-tos (`AddChoice`, `AddPart`, `AddGoTo`) and read the preview as **PreviewText** ("Park. Ava at Left, Happy, speaking; Ben at Right, Surprised, dimmed. Says: Ava: Hi!"). Or they write the tags into the Lines themselves.

## With the rest of the game

The stage names its actions for a trigger, a wire or a script: **Background** (with a background's name), **Show** and **Hide** (with a character's), **Hide All**, and **Direct** (any direction as a tag says it, without the brackets: `show Ava left happy`). Its outputs are **Background** and **Speaking** (who on it is speaking), for a Text, and **On Stage** (how many are on it). Its Advance, Skip and Auto actions work only while it has focus (its **Controllable**; see [Mixing genres](/docs/mashups/)) and the dialogue it's directing is being said, so Space can jump and A walk left in the game a visual novel scene sits inside.

What the stage shows (the background, who's on, where they stand and how they look, and fades, slides and walks under way) is saved with the scene, and so is where a dialogue has got to, so rewinding in the editor goes back into the middle of a talk and the stage as it was, and the lines done before aren't done again. In a network game each machine keeps its own stage, for its own player's reading, as each player reads dialogue on their own machine; skipping, Auto, the log and the reading menus are each machine's own too. It all works in the web player too, with a finger as the mouse.
