---
title: "Menus, HUDs and pickups"
description: "Buttons, options, pause menus and panels, button prompts, controls screens, HUD bars, pickups and scores."
---

## Menus and HUDs

**Entity → Create Button** makes a clickable button: a background sprite, a label and a **Button** component. Set its **Action** and a menu needs no code at all: **LoadScene** (with **Scene**, e.g. `Game`), **RestartScene** or **Quit**, with a **Fade** (in seconds) to fade the screen out and back in around the switch. A **Shortcut** key (Enter for Play, Esc for Back) clicks it too. Add a **ScreenAnchor** to pin anything to an edge or corner of the screen, like a score in the top-left: it stays put whatever the window's shape, while the camera follows the player or shakes. The Scene view outlines buttons' clickable areas in blue. Menus work without a mouse: the arrow keys, WASD, D-pad or stick pick a button (it lights up), and Enter, Space or the A button click it; with a controller, the top button is picked straight away, and B clicks whichever button Esc would. Untick a button's **Navigable** if it sits on a HUD during play, so moving the player doesn't pick it.

## Options menus

**Entity → Create Slider** and **Create Toggle** make an options menu with no code. Set a slider's **Setting** to **Volume**, **MusicVolume** or **SoundVolume**, or a toggle's to **Sound** (on or off) or **Fullscreen**, and it changes that setting and always shows it. The game remembers the player's choices (whether made by a slider, a toggle or a script) and puts them back next time it runs. For anything else, like a difficulty, set **Save As** to a name instead and read it from scripts with `Game.SaveData.GetFloat("difficulty")`. A slider is dragged with the mouse, turned with the wheel, or moved by its **Step** with left and right once picked; a toggle is a button whose **On Text** and **Off Text** become its label ("Sound: On"). Built games go fullscreen with F11 too.

## Pause menus and panels

**Entity → Create Panel** makes a panel: a backdrop pinned to the middle of the screen. Select it and create buttons, sliders and choices to put them on it. While the game runs it's hidden until it opens (tick **Open** to have it open from the start), so you can lay it out in the Scene view. Set its **Open On** to `Pause` and it's a pause menu with no code: Esc, P or Start opens it and stops the game, and pressing it again, Esc or the gamepad's B closes it (if the panel has a button with the Esc **Shortcut**, Esc clicks that instead). A button's **Action** opens and closes panels too: **OpenPanel** (with **Panel** naming one, like `Options`) and **ClosePanel** (Resume, Back). While a panel is open, the mouse, keyboard and gamepad only reach the controls on it, so the HUD and the menu underneath don't react; its top control (or its **First**) is picked straight away, and closing it gives the keyboard back to what was picked before. Panels stack: an options panel inside the pause menu closes with it. **Pause Game** (on by default) stops game time and runs only the scripts on open panels, and Interact doesn't reach levers, doors or dialogue behind it; untick it for a game over screen that the game goes on behind. **Close On Back** off makes the player choose their way out. **Open Message** opens it when that message is sent, with no code: `Solved` from a puzzle's goals, a `GameOver` from a player's **Death Message**, or anything a pickup, switch or script sends.

## Button prompts

Put an action's name in braces in any text and it shows the key or button that does it, on whatever the player is using: `Press {Interact}` reads "Press E" on the keyboard and "Press Y" on a gamepad, and follows the player's own bindings. Braces around anything else are left alone.

## Controls screens

A button whose **Action** is **Rebind** and whose **Input Action** is `Jump` waits for the next key or gamepad button when clicked and makes it jump from then on (a key replaces Jump's keys, a gamepad button its buttons; Esc cancels). It's remembered next time. Label it `Jump: {Jump}` and it always shows the binding (`...` while it waits). **ResetControls** puts every action back. **Entity → Create Choice** makes a button that picks one of its **Options** (one per row): left and right change it while it's picked, a click or the mouse wheel moves to the next, and its **Format** (`Difficulty: < {0} >`) labels it. With **Save As** (`difficulty`) it's remembered, for scripts to read with `Game.SaveData.GetString("difficulty")`.

## Buttons that can't be used yet

Untick a button's **Interactable** and it's drawn faded, ignores the mouse and its shortcut, and is skipped by the arrow keys. **Needs Saved** keeps it that way until something is saved under that name, for a Continue button that waits for a saved game.

## Nine-slice panels

Set a sprite's **Slice** to how much of its image is border (say 0.25 for a quarter at each edge) and its **Slice Size** to how big the border is drawn: its corners then stay crisp however you stretch its **Size**, for menu panels, dialogue boxes and buttons from one small image.

## HUD bars

**Entity → Create Bar** makes a bar that fills to its **Value** out of **Max**, which way its **Direction** says. Set its **Track** to an entity's name (`Player`, `Boss`) and it shows that entity's health with no code. When it goes down, the part just lost lingers in the **Trail** color for a moment before draining away.

## Pickups and scores

Add a **Pickup** to a coin (with a trigger collider) and it's collected when the player touches it: it sends its **Message** (`Score`) with its **Amount**, plays its **Sound**, spawns its **Effect** (a sparkle prefab, in the coin's color) and disappears. **Layers** says who can collect it (`Player`; empty is anything with a body), and **Spin** turns it while it waits. A **Counter** on a text adds up its **Message** by itself, whatever sends it (a pickup, `Game.Send("Score", 10)`, an enemy's **Death Message**), and shows it with its **Format** (`Score: {0}`, `{1}` is the best; a counter that counts goals, moves or a match board's gems has `{2}` for what it's out of and `{3}` for how many are left). With **Save Best As** (`best`) it keeps the best score between runs, so a counter on the menu with the format `Best: {1}` shows it.

The Arcade template shows all of this: a main menu, options (with a difficulty and a rebindable Fire button), a game with a pause menu, and a game over panel with Retry and Menu, fading between them. Its bullets use collision layers to fly out of the ship, and its enemy spawner is a coroutine. On a gamepad it plays twin-stick: move with the left stick, aim with the right, fire with the right trigger. The Platformer and Top-down templates' coins, gems and scores are pickups and counters, with no scripts.
