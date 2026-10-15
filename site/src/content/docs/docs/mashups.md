---
title: "Mixing genres"
description: "Genres sharing one scene: who has the input, what works on part of the scene, pausing part of it, viewports, and genres driving each other."
---

A jam themed "combine two genres" wants a match-3 board inside a platformer, a strategy map in one corner of a shooter, or a Sokoban room in an RPG. Griff's genre pieces share a scene without code: each takes input only while it has focus, works on its own part of the scene, pauses on its own, and can be seen through a camera of its own, while triggers and outputs let them drive and read each other.

## Who has the input

Everything that reads the player's keys, mouse or gamepad has a **Controllable** tick and a **Focus** action (see [Who has the player's input](/docs/players/#who-has-the-players-input)): the movements, match-3 and falling-block boards, the strategy **Commander**, the **Tactics Controls**, the **Sandbox Tools**, a **Block Builder** and an RPG **Battler**. Focus gives the input to one entity (and what's inside it) and takes it from everything else the same player plays, so a board and a platformer's player don't both move on the arrow keys. A trigger hands it over: on an arcade cabinet, "when `Play` arrives, Focus the Board", and on the board, "when `Won` arrives, Focus the Player". Scripts call `Game.Focus(entity)` and ask `Game.HasFocus(entity)`.

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

Components name what they do and what they know, so any genre's pieces drive and read any other's with no code: a **Trigger** does any action when a message arrives ("when `Matched` arrives from the Board, Fire the Cannon"; see [Triggers](/docs/story/#triggers)), a **Message Scope** keeps two boards' messages apart (see [Messages to part of the scene](/docs/story/#messages-to-part-of-the-scene)), and a Counter, a Bar, a Text or a wire shows or carries any output, such as a board's Score or a faction's Gold (see [Outputs](/docs/story/#outputs)).
