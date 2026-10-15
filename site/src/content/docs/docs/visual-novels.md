---
title: "Visual novels"
description: "A stage with backgrounds and characters at named positions, with expressions, directed by the lines of a dialogue: show, hide and move them, change the background, cut, fade or slide, with no code."
---

## A stage

**Entity → Genres → Visual Novels → Stage** makes a visual novel scene you can play straight away: a **Stage** filling the camera's view with a park, a room and a night sky to show, Ava and Ben inside it (off stage until the script brings them on), each with three expressions, a dialogue box along the bottom whose lines type themselves out and wrap, and a short script on the stage that sets the scene, brings them on, changes their faces, offers a choice and moves home. Play, and the script starts by itself. Its pictures go in **Assets/Stage**, to repaint in the Sprite Painter. **Entity → Genres → Visual Novels → Character** adds someone else, inside the stage, at a free position.

A **Stage** draws a background filling its **Size** (as much as the camera shows, for a whole screen), under everything else (its **Order**). Its **Backgrounds** are one per row, a name and what it looks like: a picture (`Park: Stage/park.png`), a color (`Night: #10183A`) or a color at the top running to another at the bottom (`Dusk: #F2A65A #3A2F5B`). **Background** is the one showing: set it in the Inspector for the one the game starts with. Its **Positions** are where characters stand, one per row, a one-word name and how far across from the stage's middle (and, after a comma, how far up): `Left: -4.5`, `Center: 0` and `Right: 4.5` to begin with, and any of your own (`FarLeft: -7, -0.5`).

## Characters and expressions

A **Character** is someone who appears on the stage: an entity inside it with a **Sprite** (how they look) and usually a **Speaker** (their name, which their lines start with). They're on stage while their entity is shown, standing at their **Position**, one of the stage's: pick it in the Inspector and they stand there. Their **Expressions** are one per row, a one-word name and either a picture for their sprite (`Happy: Stage/ava-happy.png`) or the name of a clip their **Animation** plays (`Laugh: Laugh`, a laugh that moves, or a blink). **Expression** is how they look now: set it and their sprite shows it.

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

## The Dialogue Editor

**Entity → Edit Dialogue...** opens the Dialogue Editor on the selected entity's dialogue (or that of the stage a selected character is on). It lists the lines, each with who says it and what its tags do ("Show Ava at Left, Happy"), and for the line picked:

- **Said by** and **looking**: the speaker, picked from everyone with a Speaker in the scene, and the expression they say it with, from theirs.
- **Says**: what's said, and **As written**: the whole line, tags and all, to type.
- **Stage directions**: the line's, each with a button to remove it, and below them a direction to add, built from lists of what the stage has: Show, Hide, Move or Background, the character, the position, the expression, the background and cut, fade or slide.
- **Stage at this line**: a preview of the stage as the lines above leave it (read top to bottom, whichever way the choices go), the speaker lit and the others dimmed, with the line in the box, and the same in words underneath. Directions that name something the stage doesn't have are flagged above it.

Lines are added, removed and moved up and down from there too. Every change rewrites the dialogue's **Lines**, each one undo step, so it's the same as typing them in the Inspector. It follows the selection, so it can stay open beside the scene. Agents do the same through the MCP: the `Story.EditDialogue` tool opens it, and `ui_invoke` and `ui_set` on **DialogueEditor** pick a line (`SelectLine`), set **Speaker**, **Expression** and **Said**, add a direction (`AddDirection` with `show`, `Ava`, `Left`, `Happy`) and read the preview as **PreviewText** ("Park. Ava at Left, Happy, speaking; Ben at Right, Surprised, dimmed. Says: Ava: Hi!"). Or they write the tags into the Lines themselves.

## With the rest of the game

The stage names its actions for a trigger, a wire or a script: **Background** (with a background's name), **Show** and **Hide** (with a character's), **Hide All**, and **Direct** (any direction as a tag says it, without the brackets: `show Ava left happy`). Its outputs are **Background** and **Speaking** (who on it is speaking), for a Text, and **On Stage** (how many are on it). Its Advance Action works only while it has focus (its **Controllable**; see [Mixing genres](/docs/mashups/)), so Space can jump in the game a visual novel scene sits inside.

What the stage shows (the background, who's on, where they stand and how they look, and fades, slides and walks under way) is saved with the scene, and so is where a dialogue has got to, so rewinding in the editor goes back into the middle of a talk and the stage as it was, and the lines done before aren't done again. In a network game each machine keeps its own stage, for its own player's reading, as each player reads dialogue on their own machine. It all works in the web player too, with a finger as the mouse.
