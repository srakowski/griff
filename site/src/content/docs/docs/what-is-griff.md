---
title: What is Griff
description: A 2D game engine and editor for game jams, written in C#, built around a few promises.
---

**Griff is a game engine for game jams.** It's a 2D-first engine and editor (built so 3D can follow), written in C#. It aims to be the shortest path from an idea to something people can play by Sunday night, with an editor that just works.

## Why Griff

Most engines make you stop the game to change it, lose your changes when play mode ends, and break undo in creative ways. Griff is built around a few promises:

- **The game keeps running while you change it.** Save a C# script and every instance of it is swapped in the *running* game within a moment. Public properties carry over, and the game doesn't restart. A compile error leaves the last good version running and points at the exact line.
- **Breakpoints that cost nothing, stepping, and pausing where a script throws.** Click beside a line of a script and the game pauses when it gets there, in the middle of its frame, with the values in scope to look into. Step over, into and out a line at a time, or Continue from that line. Set one while the game runs: nothing is rebuilt or restarted.
- **Rewind.** While playing, Griff keeps the last 20 seconds. Pause, drag the timeline back to just before the bug, tweak something, and resume from there.
- **The same game on every machine.** The game moves in fixed steps, a sixtieth of a second each, whatever the screen's frame rate, and the screen is drawn between the last two steps: smooth on a 144 Hz screen, and the same input gives the same result anywhere.
- **Undo covers everything.** Every change goes through one undo history, even across a script reload.
- **Play mode can't hurt your work.** Play runs on a copy of the scene. Inspect and tweak the live game freely; Stop puts everything back. **Keep Play Changes** (Ctrl+K) copies the selected entity's live values into your scene.
- **A crash costs nothing.** Unsaved scenes are written to a recovery copy every 20 seconds and offered back next time you open them.
- **Readable files.** Scenes are small, stable JSON that diff and merge cleanly in git, and a renamed or deleted script never destroys data.
- **Batteries included.** Sprites and animation, tilemaps painted in the Scene view (square, isometric or hex), 2D physics, lights and shadows, weather, particles, dialogue, menus and HUDs with no code, input players rebind in a Controls window, a Sprite Painter and a Sound Maker, and a module for each genre: platformers, top-down, match-3, falling blocks, roguelikes, tactics, strategy, first-person shooters, block worlds and contraptions.
- **Multiplayer for every kind of game**, over the local network or the internet, or with a dedicated server.
- **Agents build games with it.** While it's open, the editor serves the [Model Context Protocol](/docs/agents/), so an agent like Claude Code can make games with the same commands and components you use.
- **One click to ship.** Build Game produces a single `.exe` that carries everything, .NET included, so players install nothing.

## Templates

Every template is playable (and audible) from the first second: Platformer, Top-down, Isometric, Arcade, Strategy, Shooter, Sandbox, Contraption, Multiplayer, Puzzle, Match-3, Falling Blocks, Roguelike and Tactics, and a BunnyMark benchmark.

## Where things are written down

Griff is young, and the [README on GitHub](https://github.com/srakowski/griff#readme) is still where everything is written down in full: the editor, scripting, the architecture and the known gaps. Its contents are moving into these docs.

Griff is open source under the [MIT License](https://github.com/srakowski/griff/blob/main/LICENSE).
