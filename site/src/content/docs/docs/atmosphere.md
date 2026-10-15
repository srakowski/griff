---
title: "Rooms, light and atmosphere"
description: "Rooms, lights and shadows, weather, screen effects, scene transitions, parallax and trails."
---

## Rooms

For a game that moves screen by screen (a dungeon, a Metroidvania, Celeste's screens), lay the level out in rooms: **Entity → Create Room** makes one a screen big, and its **Size** makes it bigger. While the camera's **Follow** target is in a room, the camera keeps inside it (in place of **Stay Inside**); when the target crosses into the next, the camera slides over in **Transition** seconds, with the game held still meanwhile (untick **Freeze** to keep it going). A room can start its **Music** (crossfading from what was playing; the same music carries on), set the camera's **Ambient** light (a dark cave inside a sunny level; transparent leaves the camera's own) and send its **Message** when it's entered, the room you start in included. Out of every room the camera keeps to the last one. The Scene view outlines rooms in blue with their names.

## Lights and darkness

Set the camera's **Ambient** to a dark color and the scene goes dark: deep blue for a night, near black for a cave. Then **Entity → Create Light** (or add a **Light** to the player for a lantern, to a torch, to a glowing mushroom): it lights everything within its **Radius** in its **Color**, brightest in the middle and fading out by its **Softness** (0 is a hard-edged circle), as bright as its **Intensity**, from its **Offset** (a lantern held up). **Flicker** makes it waver like a flame (0.2 suits a torch). Lights that overlap add up. The HUD and menus are never darkened: anything pinned with a **ScreenAnchor** or riding inside the camera. The Scene view isn't darkened either, so you can see what you're placing; it shows each light as a soft glow, and a selected light's reach. `Game.LightAt(position)` says how lit a place is (0 to 1), for stealth games where guards can't see you in the shadows.

## Shadows

Tick a light's **Shadows** and walls, solid tiles (slopes included, and a hex map's hexagons) and solid colliders block it, casting shadows away from it. They're lit on the side facing the light and dark behind. What carries the light (the player holding a lantern) and anything the light is inside (a torch set into a wall) cast none, and triggers, one-way platforms and background, ladder and one-way tiles let light through. `Game.LightAt` sees shadows too, so a guard can't see a player hiding behind a pillar. The Top-down template's lantern and torches cast shadows. A frame in the dark is built without garbage, shadows and all: the sides that block light are worked out afresh each frame (from the solid tilemaps and colliders, not by looking at every entity) into lists the game keeps, and each light's shadows go into lists from the frame before. With 5,000 BunnyMark bunnies in the dark among walls and crates, a light with shadows made 451 KB of garbage a frame and took 0.36 ms of building the picture (`bunnybench --dark --profile`'s Lights row; 21 garbage collections in ten seconds); now it makes none and takes 0.1 ms, and the frame's build went from 0.55 to 0.37 ms.

## Weather

**Entity → Create Weather** fills the view with **Rain**, **Snow**, **Leaves** or **Embers** (its **Kind**), wherever the camera goes. **Amount** is how heavy it is (scripts can change it as it plays, for a storm that builds), **Wind** blows it sideways (slanting rain, drifting snow), **Speed** makes it fall faster or slower and **Tint** colors it. Its **Order** says what it falls in front of, and in a dark scene it's lit like everything else. Tick **Storm** for lightning every few seconds, with its **Thunder Sound** a moment after each flash.

## Screen effects

Add **Screen Effects** to the camera to change the look of the whole screen, HUD included: a **Vignette** darkening the edges (on at 0.4 to start), **Scanlines** and **Curvature** for an old TV, **Pixel Size** for big chunky pixels, **Aberration** for color fringes, **Grain** for film grain, **Saturation**, **Contrast**, **Brightness** and **Tint** to grade the colors, and a **Palette** to draw it all in a handheld's four greens (**GameBoy**), **Sepia** or black and white (**OneBit**). Scripts can change any of them while the game runs: tween the saturation down as the player's health runs low, or bulge the screen on a big hit.

## Scene transitions

A camera's **Transition** says how its scene covers up and uncovers a scene change (a door, a button's **Fade**, `LoadScene` with a `fade:`): **Fade** (the whole screen), **Wipe** (the color sweeps across from the left, then carries on off the right) or **Iris** (a circle closes on the player and opens out again).

## Parallax

Add a **Parallax** to a background entity and set its **Factor**: 1 moves like the rest of the world, 0 stays stuck to the camera like a distant sky, and in between is in between (far hills 0.2, near trees 0.7). It shifts everything the entity and its children draw. **Repeat X** and **Repeat Y** repeat its sprite to fill the view, so a short strip of hills never runs out. It's applied as the game is drawn, so the layer is exactly where its transform puts it when the camera is at the origin, and the Scene view shows it there.

## Trails

Add a **Trail** next to a sprite and it leaves copies of itself that fade away over **Fade** seconds while it moves faster than **Min Speed**: dashes, speed lines, ghosts, bullets with streaks. **Color** paints the copies one color, and scripts can turn **Emitting** off and on (only during a dash, say).
