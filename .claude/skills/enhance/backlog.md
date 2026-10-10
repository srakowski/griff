# Enhance backlog

`/enhance` builds the first unchecked item, then ticks it in the same commit. Items are in order: later ones build on
earlier ones, so reorder with care. Keep each item one small thing; split anything that grows.

## Next

- [ ] Add grid movement: things that step tile by tile on a tilemap, with no-code components.
- [ ] Add turn-based play: a turn order where each side moves on its own turn, alongside the existing real-time play.
- [ ] Add branching dialogue: choices inside dialogue, flags that later lines can check, and portraits.
- [ ] Add drag-and-drop: things the player can pick up with the mouse or a controller and drop onto a target.
- [ ] Add a beat clock: the music's playback position and beat events that scripts and components can follow.
- [ ] Add Sokoban-style puzzles with a Puzzle template: crates pushed on the grid, goals, undo and level restart.
- [ ] Add roguelikes with a Roguelike template: dungeons generated from a seed, turn-based enemies and items.
- [ ] Add tactics games with a Tactics template: squads that move and attack on a grid, taking turns.
- [ ] Add tile-matching boards with a Match-3 template: swap to match, cascades and refills.
- [ ] Add falling-block puzzles with a Falling Blocks template.
- [ ] Add card games with a Cards template: decks, hands, play zones and a turn-based opponent.
- [ ] Add point-and-click adventures with an Adventure template: hotspots, walking to where you click, and using items on things.
- [ ] Add visual novels with a Visual Novel template: backgrounds, characters on screen, choices and saving.
- [ ] Add rhythm games with a Rhythm template: notes timed to the music and hit judgments.
- [ ] Add fighting games with a Fighter template: hitboxes for each animation frame, blocking, special-move inputs and local versus play.
- [ ] Add isometric tilemaps and depth sorting, with an Isometric template.
- [ ] Add hex grids that work with the grid movement and turn-based play.
- [ ] Make physics bodies rotate, without breaking the components and templates that already use them.
- [ ] Make circles roll, and give polygon colliders rotating physics too.
- [ ] Make ropes swing and sag, and let welded contraptions turn.
- [ ] Add physics puzzlers with a Physics template, in the Angry Birds style.
- [ ] Add pinball with flippers and bumpers, and a Pinball template.
- [ ] Add a web export.
- [ ] Add a BunnyMark benchmark: a scene that keeps adding bouncing sprites, shows the fps and the sprite count, and records how many it holds at 60 fps.
  - Target: at least 50,000 bunnies (each an ordinary entity with a Sprite and a Body or script) at 60 fps in an exported game, with 1% lows also at 60 fps and no garbage collected per frame, so a jam game never has to think about performance. That's above Godot 3's C# result (about 27,500), and it leaves room for a real game's logic on top. If Griff falls short, profile it and fix the bottlenecks (sprite batching, per-entity overhead, allocations, physics broadphase) as part of this item; if the fixes outgrow one change, add them as items right after it. Record the result in the README.
- [ ] Add a kata runner that builds the first few 20 Games Challenge clones (Pong, Breakout, Flappy Bird, Space Invaders) and records for each what it needed: no-code components, lines of script, and what was missing.

## Done

<!-- /enhance moves finished items here: "- [x] <item> (YYYY-MM-DD)", plus a short note on anything left for later. -->
