<img src="assets/griff.png" width="128" alt="Griff's logo: a pixel-art griffin's head">

# Griff

**A game engine for game jams.** Griff is a 2D-first engine and editor (built so 3D can follow), written in C#. It aims to be the shortest path from an idea to something people can play by Sunday night, with an editor that just works.

## Why Griff

Most engines make you stop the game to change it, lose your changes when play mode ends, and break undo in creative ways. Griff is built around a few promises:

- **The game keeps running while you change it.** Save a C# script and every instance of it is swapped in the *running* game within a moment. Public properties carry over, and the game doesn't restart. A compile error leaves the last good version running and points at the exact line.
- **Rewind.** While playing, Griff keeps the last 20 seconds. Pause, drag the timeline back to just before the bug, tweak something, and resume from there.
- **Undo covers everything.** Every change goes through one undo history. That includes the history surviving a script reload: undo still targets the rebuilt components.
- **Play mode can't hurt your work.** Play runs on a copy of the scene. Inspect and tweak the live game freely; Stop puts everything back. Found the perfect jump height while playing? **Keep Play Changes** (Ctrl+K) copies the selected entity's live values into your scene.
- **A crash costs nothing.** Unsaved scenes are written to a recovery copy every 20 seconds and offered back next time you open them.
- **Readable files.** Scenes are small, stable JSON (`"position": [1.5, -2, 0]`) that diff and merge cleanly in git. A deleted or renamed script never destroys data; its values are kept and come back with it.
- **Batteries included.** Sprites and sprite-sheet animation with named clips, tilemaps you paint right in the Scene view (with autotiles that fit themselves to their neighbors, and slopes to run up and down), pathfinding around their walls, players that run, jump, wall-jump, dash and climb ladders or move top-down with no code, crates to push, springs, crumbling ledges, platforms you drop through, particles, prefabs, timers, coroutines, tweens with easing, messages between scripts, text in your own fonts, 2D physics with boxes, circles, bounce, triggers, collision layers, raycasts, moving platforms that follow a path, wall checks and coyote time, a follow camera that stays inside the level, rooms the camera slides between, darkness lit by flickering torches and lanterns that cast shadows, rain, snow, falling leaves and rising embers with lightning storms, screen effects (old-TV scanlines and curved glass, chunky pixels, a handheld's four greens, vignettes, color grading), parallax backgrounds, top-down depth sorting, camera shake, screen flashes, slow motion, health with hit flashes and invincibility, health bars, damage on touch, checkpoints and respawning, enemies that patrol or chase (around walls too), swords and other close-up attacks for players and enemies, turrets and guns, doors and teleporters, dialogue with signs and characters, levers, pressure plates and buttons that open gates and bring out bridges, keys, locked doors and an inventory, animated tiles, afterimage trails, hit stop, pickups and score counters that need no code, spawners and lifetimes, typewriter dialogue text, debug drawing, save data, input with named actions you set up in a Controls window and players can rebind (and keep), jump buffering, twin-stick aiming, rumble and name entry, a Sprite Painter for pixel-art sprites, animation frames and tilesets and a Sound Maker for retro sound effects and looping music right in the editor, sound and music with volume settings, sounds at any pitch, sounds that come from where things are, looping sounds in the level, music that crossfades, menus and HUDs with buttons, sliders, toggles and choices that work with the mouse, keyboard or a controller, pause menus and panels that need no code, button prompts that switch between keyboard and controller, a controls screen players rebind from, options that are remembered, stretchable nine-slice panels, screen-pinned text, fades, wipes and irises between scenes, real-time strategy with units you select and order about, workers that gather gold and wood and put up buildings, barracks that train armies, towers, fog of war, a minimap and a computer opponent, first-person shooters whose levels you paint top-down and play standing in them (textured walls, mouse look, guns with ammo, enemies and pickups that face you, sliding doors), block worlds made from a seed to dig up and build in (caves, ores, trees, sand that falls, a hotbar, crafting at workbenches and furnaces, torch-lit darkness underground, worlds kept between runs, and digging in first person too), contraptions built while the game runs (grab, throw and freeze props, spawn them, weld and rope them together, undo) wired up with buttons, sensors, logic gates, clocks, counters, lamps, displays and thrusters, multiplayer for every kind of game (host or join over the local network or the internet, or run a dedicated server; each player moves their own character at once while the host runs the rest, and Griff's components work for every player, from platformers and shooters to strategy, block worlds and big worlds that only send each player what's near them), and Platformer, Top-down, Arcade, Strategy, Shooter, Sandbox, Contraption and Multiplayer templates that are playable (and audible) from the first second.
- **One click to ship.** Build Game produces a single `.exe` that carries everything, .NET included, so players install nothing. Build Small Game makes a 4 MB `.exe` for players who have the .NET Desktop Runtime.

## Using the editor

| Action | Shortcut |
| --- | --- |
| New project (pick a template) | Ctrl+Shift+N |
| Open project | Ctrl+O |
| Save scene | Ctrl+S |
| Undo / redo | Ctrl+Z / Ctrl+Y |
| Create empty entity | Ctrl+Alt+N |
| Rename, duplicate, delete | F2, Ctrl+D, Delete |
| Play / pause / step / stop | F5 / F6 / F10 / Shift+F5 |
| Play with 2 players (a window for player 2) | Ctrl+F5 |
| Cut / copy / paste entities | Ctrl+X / Ctrl+C / Ctrl+V |
| Build game / small game | Ctrl+B / Ctrl+Shift+B |

In the **Scene view**:
- Scroll to zoom.
- Middle-drag, right-drag or Alt-drag to pan.
- Click to select; Ctrl- or Shift-click adds one or takes it away.
- Drag from empty space to select everything inside the box (hold Ctrl or Shift to add to the selection).
- Drag to move: everything selected moves together, as one undo step (hold Ctrl to snap the one picked last to the grid).
- Press F to frame the selection.
- Arrow keys nudge everything selected by half a unit (Shift for a tenth).
- Drop an image to create a sprite, or a prefab to place a copy of it.

In the **Hierarchy**:
- Click to select; Ctrl-click adds or takes one away, Shift-click selects every row from the last one clicked (Ctrl+Shift-click adds them).
- Delete, duplicate, cut, copy and paste work on everything selected, as one undo step. Duplicates go right after their originals, pastes after the selected entity, and the copies end up selected.
- Drag a row onto another to put it inside, or above or below it.
- The Inspector shows the last one picked, and the Scene view outlines them all.

**Tilemaps.** **Entity → Create Tilemap**, then set its **Image** to a tileset and its **Columns** and **Rows** to the tileset's layout. While a tilemap is selected, the Scene view paints:
- Drag to paint, Shift-drag to erase. Each stroke is one undo step.
- Pick a tile from the palette at the top left, with `[` and `]`, or Ctrl-click a painted tile.
- Paint past the left or top edge and the map grows that way.
- Press Esc (or select something else in the Hierarchy) to stop painting.

The map's tiles are also plain text in the Inspector, one row per line (`.` is empty, `0`–`9`, `a`–`z`, `A`–`Z` are tiles 0–61), so you can type a level out or paste one in. With **Solid** on, tiles are walls and floors, except:
- **Background Tiles** never collide: grass, windows, signs. List tile numbers and ranges, e.g. `10-15, 20`.
- **One Way Tiles** are platforms you jump up through and land on.
- **Ladder Tiles** never collide, and players climb them.
- **Slope Up Tiles** (`/`) and **Slope Down Tiles** (`\`) are 45 degree slopes, solid below the line from corner to corner. Players and enemies walk up and down them without leaving the ground, land on them and jump from them; the tall side is a wall, and the flat bottom is a ceiling. Raycasts see them too, so patrolling enemies walk them.

**Autotiles.** Set **Auto Tiles** to a run of 16 tiles in the tileset (`16-31`, or just `16`) and paint with any of them: each cell shows the piece that joins up with its neighbors from the same run, in the Scene view and the game, so grass edges and cave walls take care of themselves. The run's tiles go in order of which sides join, counting up as 1, right 2, down 4 and left 8: the first is a lone block, the second joins only above, and the last joins on every side.

The Scene view outlines what collides: one-way runs get a dashed outline with a solid top edge, and slopes a triangle.

**Menus and HUDs.** **Entity → Create Button** makes a clickable button: a background sprite, a label and a **Button** component. Set its **Action** and a menu needs no code at all: **LoadScene** (with **Scene**, e.g. `Game`), **RestartScene** or **Quit**, with a **Fade** (in seconds) to fade the screen out and back in around the switch. A **Shortcut** key (Enter for Play, Esc for Back) clicks it too. Add a **ScreenAnchor** to pin anything to an edge or corner of the screen, like a score in the top-left: it stays put whatever the window's shape, while the camera follows the player or shakes. The Scene view outlines buttons' clickable areas in blue. Menus work without a mouse: the arrow keys, WASD, D-pad or stick pick a button (it lights up), and Enter, Space or the A button click it; with a controller, the top button is picked straight away, and B clicks whichever button Esc would. Untick a button's **Navigable** if it sits on a HUD during play, so moving the player doesn't pick it.

**Options.** **Entity → Create Slider** and **Create Toggle** make an options menu with no code. Set a slider's **Setting** to **Volume**, **MusicVolume** or **SoundVolume**, or a toggle's to **Sound** (on or off) or **Fullscreen**, and it changes that setting and always shows it. The game remembers the player's choices (whether made by a slider, a toggle or a script) and puts them back next time it runs. For anything else, like a difficulty, set **Save As** to a name instead and read it from scripts with `Game.SaveData.GetFloat("difficulty")`. A slider is dragged with the mouse, turned with the wheel, or moved by its **Step** with left and right once picked; a toggle is a button whose **On Text** and **Off Text** become its label ("Sound: On"). Built games go fullscreen with F11 too.

**Pause menus and panels.** **Entity → Create Panel** makes a panel: a backdrop pinned to the middle of the screen. Select it and create buttons, sliders and choices to put them on it. While the game runs it's hidden until it opens (tick **Open** to have it open from the start), so you can lay it out in the Scene view. Set its **Open On** to `Pause` and it's a pause menu with no code: Esc, P or Start opens it and stops the game, and pressing it again, Esc or the gamepad's B closes it (if the panel has a button with the Esc **Shortcut**, Esc clicks that instead). A button's **Action** opens and closes panels too: **OpenPanel** (with **Panel** naming one, like `Options`) and **ClosePanel** (Resume, Back). While a panel is open, the mouse, keyboard and gamepad only reach the controls on it, so the HUD and the menu underneath don't react; its top control (or its **First**) is picked straight away, and closing it gives the keyboard back to what was picked before. Panels stack: an options panel inside the pause menu closes with it. **Pause Game** (on by default) stops game time and runs only the scripts on open panels, and Interact doesn't reach levers, doors or dialogue behind it; untick it for a game over screen that the game goes on behind. **Close On Back** off makes the player choose their way out.

**Controls.** **Game → Controls...** lists every input action the game has and the keys, mouse buttons and gamepad buttons that do each. Pick an action, click **Press a Key** and press one (or click the button with a mouse button) to add it; controller buttons come from **Add Button** with no controller needed, or **Press on Controller** with one. A key that another action also uses is pointed out, the × on a key takes it off, and **Reset to Default** puts one of Griff's six actions (Jump, Fire, Dash, Attack, Interact, Pause) back; they can be rebound but keep their names, since components use them. **Add Action** makes one of the game's own (`Shield`), which scripts read with `Input.WasPressed("Shield")` and button prompts and Rebind buttons work with. Changes are saved in `project.griff` as you make them (only what differs from Griff's defaults), reach a running game straight away, and go into exported games. A player's own Rebind choice still wins over them, and a script's `Input.Bind` over both; the window says when a choice you made while testing is in the way (**Game → Clear Save Data** clears it).

**Button prompts.** Put an action's name in braces in any text and it shows the key or button that does it, on whatever the player is using: `Press {Interact}` reads "Press E" on the keyboard and "Press Y" on a gamepad, and follows the player's own bindings. Braces around anything else are left alone.

**Controls screens.** A button whose **Action** is **Rebind** and whose **Input Action** is `Jump` waits for the next key or gamepad button when clicked and makes it jump from then on (a key replaces Jump's keys, a gamepad button its buttons; Esc cancels). It's remembered next time. Label it `Jump: {Jump}` and it always shows the binding (`...` while it waits). **ResetControls** puts every action back. **Entity → Create Choice** makes a button that picks one of its **Options** (one per row): left and right change it while it's picked, a click or the mouse wheel moves to the next, and its **Format** (`Difficulty: < {0} >`) labels it. With **Save As** (`difficulty`) it's remembered, for scripts to read with `Game.SaveData.GetString("difficulty")`.

**Buttons that can't be used yet.** Untick a button's **Interactable** and it's drawn faded, ignores the mouse and its shortcut, and is skipped by the arrow keys. **Needs Saved** keeps it that way until something is saved under that name, for a Continue button that waits for a saved game.

**Panels.** Set a sprite's **Slice** to how much of its image is border (say 0.25 for a quarter at each edge) and its **Slice Size** to how big the border is drawn: its corners then stay crisp however you stretch its **Size**, for menu panels, dialogue boxes and buttons from one small image.

**HUD bars.** **Entity → Create Bar** makes a bar that fills to its **Value** out of **Max**, which way its **Direction** says. Set its **Track** to an entity's name (`Player`, `Boss`) and it shows that entity's health with no code. When it goes down, the part just lost lingers in the **Trail** color for a moment before draining away.

**Pickups and scores.** Add a **Pickup** to a coin (with a trigger collider) and it's collected when the player touches it: it sends its **Message** (`Score`) with its **Amount**, plays its **Sound**, spawns its **Effect** (a sparkle prefab, in the coin's color) and disappears. **Layers** says who can collect it (`Player`; empty is anything with a body), and **Spin** turns it while it waits. A **Counter** on a text adds up its **Message** by itself, whatever sends it (a pickup, `Game.Send("Score", 10)`, an enemy's **Death Message**), and shows it with its **Format** (`Score: {0}`, `{1}` is the best). With **Save Best As** (`best`) it keeps the best score between runs, so a counter on the menu with the format `Best: {1}` shows it.

The Arcade template shows all of this: a main menu, options (with a difficulty and a rebindable Fire button), a game with a pause menu, and a game over panel with Retry and Menu, fading between them. Its bullets use collision layers to fly out of the ship, and its enemy spawner is a coroutine. On a gamepad it plays twin-stick: move with the left stick, aim with the right, fire with the right trigger. The Platformer and Top-down templates' coins, gems and scores are pickups and counters, with no scripts.

**Players with no code.** **Entity → Create Platformer Player** makes a player that runs and jumps straight away: its **Platformer Movement** runs at **Speed**, gets up to it and stops at its **Acceleration** and **Deceleration** (less of both in the air, by **Air Control**), and jumps **Jump Height** high with the Jump action. Jumps are forgiving: one pressed a moment before landing (**Jump Buffer**) or a moment after running off a ledge (**Coyote Time**) still happens, and letting go early makes a short hop. Falling is a little heavier than rising (**Fall Multiplier**), up to **Max Fall Speed**. Tick what the game needs: **Air Jumps** (1 is a double jump), **Wall Slide** and **Wall Jump**, and **Dash** (the Dash action, Shift or the gamepad's B: **Dash Speed** for **Dash Time**, once in the air until landing). Down and Jump on a one-way platform drops through it. It flips its sprite to face where it's going, plays sounds and a **Dust Effect** if you give it some, and if it has an **Animation** with clips named `Idle`, `Run`, `Jump`, `Fall`, `Climb`, `Dash` or `WallSlide`, it plays them itself. **Entity → Create Top-down Player** makes one with **Top-down Movement** instead: eight directions (or **Four Way**), an optional dash, and `Idle`/`Run` clips (with `IdleUp`, `RunDown` and so on, facing that way). It ignores gravity. Untick **Controllable** for a cutscene. When a player is selected, the Scene view draws how far it jumps each way.

**Ladders, crates, springs and crumbling ledges.** **Entity → Create Ladder** makes a ladder (a trigger with a **Ladder**); up and down climb it without gravity, and Jump leaves it. Put a one-way platform at the top to stand on: climbing passes through it both ways, so down from the top climbs back down. **Entity → Create Crate** makes a crate with a **Pushable**: players walk into it to push it at its **Speed** (0.6 of theirs), along the ground in a platformer and any way in a top-down game, and it pushes crates in front of it until a wall stops them. It's on the `Crate` layer, so a pressure plate with `Crate` in its **Layers** can wait for it. **Entity → Create Spring** makes a spring: a **Bouncer** sends whatever lands on it flying at its **Strength** in its **Direction** (sideways makes a bumper; on a trigger it's a boost pad), giving a player its air jumps and dash back, and the Scene view draws where it sends things. Add a **Crumble** to a platform and once the player stands on it, it shakes for **Delay** seconds, falls away and comes back **Respawn** seconds later, once nobody's in the way.

**Cameras.** Add a **Follow** to the camera and set its **Target** (`Player`) and it follows with no code. **Smoothing** sets how quickly it catches up (0 sticks exactly), **Offset** shifts the view (say `0, 2` to see more above the hero), **Dead Zone** is a box the target can move about in without the camera moving (a tall one keeps a platformer's camera still through jumps), and **Look Ahead** leads the way the target is moving. Set **Stay Inside** to the name of the level's tilemap, a big box collider or a backdrop sprite, and the camera never shows past its edges. Follow works on anything, not just cameras: a pet that trails its owner. For top-down games, tick the camera's **Sort By Y** so whatever is lower on screen draws in front.

**Rooms.** For a game that moves screen by screen (a dungeon, a Metroidvania, Celeste's screens), lay the level out in rooms: **Entity → Create Room** makes one a screen big, and its **Size** makes it bigger. While the camera's **Follow** target is in a room, the camera keeps inside it (in place of **Stay Inside**); when the target crosses into the next, the camera slides over in **Transition** seconds, with the game held still meanwhile (untick **Freeze** to keep it going). A room can start its **Music** (crossfading from what was playing; the same music carries on), set the camera's **Ambient** light (a dark cave inside a sunny level; transparent leaves the camera's own) and send its **Message** when it's entered, the room you start in included. Out of every room the camera keeps to the last one. The Scene view outlines rooms in blue with their names.

**Lights and darkness.** Set the camera's **Ambient** to a dark color and the scene goes dark: deep blue for a night, near black for a cave. Then **Entity → Create Light** (or add a **Light** to the player for a lantern, to a torch, to a glowing mushroom): it lights everything within its **Radius** in its **Color**, brightest in the middle and fading out by its **Softness** (0 is a hard-edged circle), as bright as its **Intensity**, from its **Offset** (a lantern held up). **Flicker** makes it waver like a flame (0.2 suits a torch). Lights that overlap add up. The HUD and menus are never darkened: anything pinned with a **ScreenAnchor** or riding inside the camera. The Scene view isn't darkened either, so you can see what you're placing; it shows each light as a soft glow, and a selected light's reach. `Game.LightAt(position)` says how lit a place is (0 to 1), for stealth games where guards can't see you in the shadows.

**Shadows.** Tick a light's **Shadows** and walls, solid tiles (slopes included) and solid colliders block it, casting shadows away from it. They're lit on the side facing the light and dark behind. What carries the light (the player holding a lantern) and anything the light is inside (a torch set into a wall) cast none, and triggers, one-way platforms and background, ladder and one-way tiles let light through. `Game.LightAt` sees shadows too, so a guard can't see a player hiding behind a pillar. The Top-down template's lantern and torches cast shadows.

**Weather.** **Entity → Create Weather** fills the view with **Rain**, **Snow**, **Leaves** or **Embers** (its **Kind**), wherever the camera goes. **Amount** is how heavy it is (scripts can change it as it plays, for a storm that builds), **Wind** blows it sideways (slanting rain, drifting snow), **Speed** makes it fall faster or slower and **Tint** colors it. Its **Order** says what it falls in front of, and in a dark scene it's lit like everything else. Tick **Storm** for lightning every few seconds, with its **Thunder Sound** a moment after each flash.

**Screen effects.** Add **Screen Effects** to the camera to change the look of the whole screen, HUD included: a **Vignette** darkening the edges (on at 0.4 to start), **Scanlines** and **Curvature** for an old TV, **Pixel Size** for big chunky pixels, **Aberration** for color fringes, **Grain** for film grain, **Saturation**, **Contrast**, **Brightness** and **Tint** to grade the colors, and a **Palette** to draw it all in a handheld's four greens (**GameBoy**), **Sepia** or black and white (**OneBit**). Scripts can change any of them while the game runs: tween the saturation down as the player's health runs low, or bulge the screen on a big hit.

**Scene transitions.** A camera's **Transition** says how its scene covers up and uncovers a scene change (a door, a button's **Fade**, `LoadScene` with a `fade:`): **Fade** (the whole screen), **Wipe** (the color sweeps across from the left, then carries on off the right) or **Iris** (a circle closes on the player and opens out again).

**Parallax.** Add a **Parallax** to a background entity and set its **Factor**: 1 moves like the rest of the world, 0 stays stuck to the camera like a distant sky, and in between is in between (far hills 0.2, near trees 0.7). It shifts everything the entity and its children draw. **Repeat X** and **Repeat Y** repeat its sprite to fill the view, so a short strip of hills never runs out. It's applied as the game is drawn, so the layer is exactly where its transform puts it when the camera is at the origin, and the Scene view shows it there.

**Health and hazards.** Add a **Health** to anything that can be hurt and set its **Max**. After a hit it flashes white and blinks, and can't be hurt again for its **Invincible Time**; at zero it dies, sends its **Death Message** (say `PlayerDied`) and is destroyed (untick **Destroy On Death** to play a death animation first). Add a **DamageOnTouch** to whatever hurts: spikes and lava (it keeps hurting while you stand in it), enemies, bullets. Its **Layers** says who it hurts (`Player` on spikes, `Enemy` on the player's bullets; empty hurts anything with health), **Knockback** pushes the target away, and **Destroy On Hit** spends a bullet. A collider on a child entity counts for the nearest Health above it, so a sword or a hurtbox can be its own child, and it never hurts its owner. The Arcade template's ship has three hearts, and its enemies and bullets use both.

**Moving platforms.** Add a **Mover** to a platform (or a saw, a lift, a gate) and give it **Points** to visit, as offsets from where it's placed: `4 0` goes four units right and back, `0 6` is a lift, `4 0; 4 3; 0 3` goes round a square. It moves at **Speed**, waits **Wait** seconds at each point and eases in and out of them (untick **Smooth** for a steady saw). **Mode** is **PingPong** (there and back the same way), **Loop** (straight back to the start and round again) or **Once** (a door sliding open). Whatever stands on it rides along. Its **Time** is how far into its trip it is: give a row of platforms different times so they don't move in step. The Scene view draws each mover's path. Make a lift a one-way platform so players can walk through it at the bottom.

**Enemies.** A **Patrol** walks its body back and forth at **Speed**, turning at walls and, with **Turn At Edges**, before walking off a ledge. A **Chase** goes after its **Target** (`Player`) once it's within **Range**, and gives up once it's **Give Up** further away than that. With **Needs Sight** it has to see the target first, past any walls; with gravity it runs left and right after it; without, it heads straight there, or finds its way around a tilemap's walls if **Around** names it (`Level`). **Stop Distance** keeps it back, for an enemy that shoots. Give an entity both and it patrols until it spots you. Both flip the sprite to face where they're going (draw it facing right). Add a **DamageOnTouch** and they hurt.

**Swords and close-up attacks.** Select the player and choose **Entity → Add Attack**: it gets an **Attack** and a `Hitbox` child to its right, a trigger on the `Attack` layer with a **DamageOnTouch** that hurts enemies. Pressing the Attack action (X, L, the right mouse button or the gamepad's X) winds up for **Windup** seconds, brings the hitbox out for **Active Time** seconds (it's hidden the rest of the time), then rests for **Cooldown**; a press meanwhile swings as soon as it can. Each swing hurts each thing once, however long it lingers on it, and a hit freezes the game for **Hit Stop** seconds so it lands hard. The hitbox swings the way its owner faces (laid out facing right, it's mirrored to face left, and turned up and down in a top-down game). **Lunge** steps forward as it strikes, a **Sound** plays, and an **Animation** with an `Attack` clip (`AttackUp` and `AttackDown` too, top-down) plays it. An enemy's attack has no **Action**: it swings at its **Target** (`Player`) once it's within **Range**, turning to face it first, so a knight is a **Patrol**, a **Chase** and an **Attack** with a hitbox that hurts the `Player` layer.

**Turrets and guns.** A **Shooter** fires its **Prefab** (`Bullet`, which needs a Body) every **Interval** seconds at **Speed**: at its **Target** while it's within **Range**, or in its **Direction**. **Count** and **Spread** fire a fan of shots (or with one shot, wobble its aim), **Offset** is where shots appear, and a **Sound** plays with each volley. Set its **Action** to `Fire` and it's the player's gun instead: it fires while Fire is held, towards the right stick or the mouse. Shots are turned to face the way they fly.

**Checkpoints.** Tick a player's **Health**'s **Respawn** and losing its hit points doesn't destroy it: it disappears for **Respawn Delay** seconds, then comes back with full health (and a moment of invincibility) at the last **Checkpoint** it touched, or where it started. A checkpoint is a trigger with a **Checkpoint**: touching it makes it the **Active** one, turns its sprite its **Active Tint**, plays its **Sound** and sends its **Message**. Bottomless pits and lava are a **DamageOnTouch** with **Kill** ticked, which kills straight through invincibility. Cameras following the player jump straight to where it comes back.

**Doors.** A **Door** on a trigger takes whoever touches it (on its **Layers**, `Player`) to its **Scene** (the level's exit) or, with no scene, to the entity named in its **Target** (a teleporter, a door into a house), fading out and in over **Fade** seconds. Tick **Needs Interact** and it waits for the Interact action (E, Enter or the gamepad's Y). Having arrived through one, you have to step off before a door takes you anywhere again, so a pair of portals never sends you back and forth. **Prompt** names an entity (a "Press E" text, its child) that shows only while someone can use it.

**Dialogue with characters.** **Entity → Create Dialogue Box** makes a panel at the bottom of the screen whose text types itself out. Give a sign or a character a trigger collider and a **Dialogue**: its **Lines** (one per row) are said in the box when the player presses Interact beside it, or straight away with **Start** set to **Touch**. Interact finishes the line being typed, then shows the next; after the last the box hides itself and it sends its **Message** (`TalkedToKing`). **Pause Game** stops everything while it's said, **Once** says it only the first time, and **Prompt** works like a door's. The box is hidden while the game runs until someone talks.

**Switches.** **Entity → Create Switch** makes a lever. Name what it opens in its **Targets** (commas between names): while it's on, a **Mover** moves (a gate set to **Once** slides open, and closes again when it's switched off), a **Spawner** or **Shooter** runs, particles emit, an animation plays and a **Door** unlocks; anything else simply appears. **Hides** names what disappears while it's on: a barrier, a wall of spikes. It all starts switched off, so a bridge a lever brings out isn't there until it's pulled. Its **Mode** is **Lever** (Interact flips it, with a **Prompt** like a door's), **Plate** (on while something on its **Layers** stands on it: `Player, Crate` for crates too) or **Button** (on for good once touched; shoot it by setting **Layers** to `Bullet`). **On Time** turns it off again after that many seconds, for a timed button. It turns its sprite its **On Tint** (and its **On Frame**, for a lever drawn both ways), plays its **Sound** and sends its **On Message** and **Off Message**. If several switches name the same thing, it's on only while all of them are, so a gate can need two plates held down. Names are looked for inside the switch first, so a prefab can switch its own parts; the Scene view draws a selected switch's links.

**Keys and inventory.** Give a **Pickup** an **Item** (`Key`) and whoever collects it carries it in an **Inventory** (one is added to them if they have none). A **Door** with a **Key** stays shut to anyone without one, and the first trip through uses it up; a switch with a **Key** works the same, so a locked block is a solid box with a **Button** switch that needs a key and **Hides** itself. A **Counter** with an **Item** shows how many the player holds (`Keys: {0}`). Inventories are saved with the scene, so rewinding gives back the keys picked up since, and you can start a playtest already holding a key by typing `Key` into the player's **Items**.

**Animated tiles.** Set a tilemap's **Animated Tiles** to runs of tiles side by side in its tileset and how many frames a second, `16-19 @6`, and every cell painted with one of them plays through the run while the game runs: water, lava, torches. Paint neighbors with different frames of the run and they won't move in step.

**Trails.** Add a **Trail** next to a sprite and it leaves copies of itself that fade away over **Fade** seconds while it moves faster than **Min Speed**: dashes, speed lines, ghosts, bullets with streaks. **Color** paints the copies one color, and scripts can turn **Emitting** off and on (only during a dash, say).

Every template has a pause menu. Both the Platformer and Top-down players carry a sword (X). The Platformer template has a lift up to a ledge where a slime patrols (two swings defeat it) and a lever brings out a bridge with more coins, a spring up to two ledges that crumble under you, a checkpoint flag, a sign to read, and a player with three hearts who comes back at the flag. The Top-down template is a dim dungeon lit by flickering torches and the player's lantern; in it a bat chases you once it sees you, portals take you across the room, there's someone to talk to, a key across the room opens a vault, and a crate pushed onto a plate brings out a bonus gem. Both templates' players move with no script and dash with Shift, leaving streaks; the Arcade template's bullets leave streaks too, and its scripts are the place to see how a game is coded.

**Real-time strategy.** The **Strategy** template is a whole real-time strategy game made of no-code components: blue against the computer's red, each with a town hall, workers and soldiers, on a map with gold mines, trees, rocks, fog of war and a pause menu. The pieces:
- **Faction**, one per side: its **Team** name (everything with that Team belongs to it; different names are enemies), its **Color**, and the **Resources** it has to spend, kept as text (`Gold 400, Wood 150`) so they show and rewind like anything else. A team with no buildings left is beaten; it sends its **Defeat Message** with the team's name.
- **Unit**, anything that can be selected and sent about: its **Team**, **Speed**, **Radius** and **Sight**, and, for training it, its **Cost** (`50 Gold, 25 Wood`), **Train Time**, **Supply** and **Hotkey**. Units find their way around buildings, resources, rocks and solid tiles on their own, keep apart rather than stack, spread out when sent together, and fight back when something armed shoots at them.
- **Fighter**, for units and towers: **Damage**, **Range** (edge to edge, so a melee soldier's is small), **Cooldown**, and a **Projectile** prefab (an arrow) with its **Projectile Speed**, or none to hit straight away. Fighters attack enemies that come within their unit's sight by themselves, the closest first, armed ones (and towers) a little before the rest.
- **Worker**, which makes a unit gather and build: its **Carry** and **Gather Time**, and the building prefabs it **Builds** (`Farm, Barracks, Tower`). Sent to a resource it fetches load after load to the nearest depot, moves on to more of the same when that runs out, and goes back to work after putting up a building.
- **Resource**, something to gather: its **Kind** (`Gold`, `Wood`, or anything else; a Faction's Resources use the same names) and the **Amount** left. It's gone when it's empty.
- **Building**, which stands still and takes up its **Size**: **Team**, **Sight**, **Cost**, **Build Time**, the **Supply** it provides, **Depot** (workers bring resources here) and **Hotkey**. Placed by a worker, it starts at **Progress** 0, see-through, and only trains, takes resources, provides supply or shoots once it's finished. A **Fighter** makes it a tower.
- **Producer**, on a building, **Trains** units (`Soldier, Archer`, prefabs with a Unit), up to **Queue Limit** at once, paying for each as it's queued (Esc or X cancels the last one, giving the cost back). Trained units walk out to the rally point, or start gathering if it was set on a resource. Sprites on entities called `Team Color` inside a unit or building take on its team's color, so one prefab serves every side.
- **Commander**, on the camera, is the player's controls and HUD: a bar of resources and supply along the top, and along the bottom a **minimap**, what's selected (with health, what it's doing and what it's training) and a **command card** of buttons with hotkeys. Its **Map Center** and **Map Size** bound the camera, the minimap and pathfinding. **Victory Panel** and **Defeat Panel** name panels it opens when the game is won or lost.
- **FogOfWar**, next to the Commander, hides what the player's team can't see: black where it has never been, dimmed where it has been but can't see now, with enemy units there hidden.
- **Opponent**, next to a Faction, lets the computer play it: it keeps its **Workers** gathering, trains more, puts up what it **Builds** (and farms when short of supply), trains its **Army**, one every **Train Every** seconds (0 as fast as it can pay; higher is an easier game), and once it's past **First Attack** and has **Attack With** soldiers idle, sends them to the nearest enemy building, waiting for **Wave Growth** more each time.

**First-person shooters.** The **Shooter** template is a first-person shooter made of no-code components: fight out of a dark base past grunts that charge and robots that throw fireballs, with a pistol, a shotgun to find in the armory, medkits, doors that slide open as you come near, a blue key for the locked way out, lamps lighting the corridors, a minimap and a pause menu. The level is an ordinary tilemap painted top-down in the Scene view; in play mode you stand in it. The pieces:
- **First Person View**, on the camera, draws the game through the **Viewer**'s eyes (the entity called `Player` by default). Solid tilemaps are walls, textured with their tiles (and shaded by side), as tall as **Wall Height**; sprites stand on the floor and always face the viewer (a pickup with **Spin** turns in place), hidden behind any wall that's nearer; particles hang in the air. **Field Of View**, **Eye Height** and **Columns** (strips across the screen: fewer is chunkier) shape the view, **Floor** and **Ceiling** color it, and everything fades into the **Fog** color by **Fog Distance**. In a dark scene (the camera's **Ambient**), lights light the walls, floor and sprites near them. It also draws the weapon in hand, a **Crosshair**, a **Minimap** of the walls, enemies and pickups, and a **Hurt Flash** when the viewer loses health. The HUD (anything with a **Screen Anchor**) goes on top as usual. The viewer looks the way its Rotation Z points: 0 is to the right on the map.
- **First Person Movement** walks (W and S, or up and down) and strafes (A and D) at **Speed**, or **Run Speed** with Shift, and turns with the left and right arrows at **Turn Speed**. With **Mouse Look**, clicking the game hands it the mouse (hidden, and kept inside the game) to turn with, **Mouse Sensitivity** degrees a pixel, and to look up and down a little with **Look Up Down**; Esc, pausing or switching away gives it back. The controller's left stick walks and strafes and the right stick turns. **Step Sound** plays every **Step Length** units walked.
- **Weapon**, on the player, shoots where it looks when **Action** (Fire) is pressed, or held for an **Automatic** one, up to **Fire Rate** times a second: **Pellets** bullets a shot, scattered across **Spread** degrees and reaching **Range**, each taking **Damage** from whatever with Health they hit first (every pellet that hits one thing lands as one blow) and pushing it back by **Knockback**, with an **Impact Effect** where they land. A **Projectile** prefab is launched at **Projectile Speed** instead. Each shot uses up one of the player's **Ammo** item (`Bullets`), so a **Pickup** with that Item refills it and a **Counter** with that Item shows it; with none left it plays its **Empty Sound**. A weapon with **Needs Item** (`Shotgun`) can't be used until a pickup gives that item, and is taken out the moment one does. Several weapons on the player are picked with the number keys (their **Slot**), the mouse wheel or the controller's Y. In hand it's its **Image** (or a simple gun in its **Color**) at **Size**, swapping to **Fire Image** for a moment as it fires, with a **Muzzle Flash**, a **Kick** and a screen **Shake**.
- **Billboard**, on a sprite, lifts it off the floor in first person (**Lift**: a lamp on the ceiling, a fireball at chest height), **Bob**s it up and down, or **Scale**s it.
- **Block**, on an entity with a Box Collider, draws it as a box of wall instead of a sprite, covered with its sprite's image (or color): doors, gates, pillars. Give it a **Mover** and a **Switch** to slide it open, or let a switch hide it.

Three smaller additions came with the shooters: a **Pickup**'s **Heal** gives back health (a medkit is left alone while health is full), **Damage On Touch**'s **Destroy On Wall** breaks a shot on whatever solid it hits, and a **Shooter**'s **Needs Sight** holds fire while a wall is in the way.

The controls: click a unit or building to select it (double-click picks every unit of its kind on screen), drag a box round units, and hold Shift to add to the selection. Right-click to send what's selected: on the ground to walk there, on an enemy to attack it, on a resource to gather it, on a building going up to help build, and with a building selected, to set its rally point. Hotkeys follow the command card: **A** then a click attack-moves (fighting whatever's met on the way), **M** then a click moves, **S** stops, **H** holds position; with workers, a building's hotkey and a click places it (Shift places more); with a building, a unit's hotkey trains it. Ctrl and a number keeps the selection as a group, the number picks it again, and twice brings the camera to it. The arrow keys, the screen's edges and the minimap move the camera, the mouse wheel zooms, and Esc cancels placing or targeting before it opens the pause menu.

**Block worlds.** The **Sandbox** template is a world to dig and build in, Terraria style: rolling hills over caves full of coal, iron and gold, trees, sand, and a floor of bedrock, with a hotbar, crafting, and torches to light the dark underground. All of it is no-code components on a tilemap and the player:
- **World Generator**, on a tilemap, fills it with a world made from its **Seed** each time the game starts (**Generate On Play**; untick it to keep and paint on the map as it is). The same seed always makes the same world, and 0 makes a new one every time. **Width** and **Height** size it; **Surface**, **Hills** and **Hill Width** shape the ground; **Grass Tile**, **Dirt Tile** (**Dirt Depth** deep) and **Stone Tile** fill it; **Caves** hollows out tunnels and caverns, more of them deeper down; **Ores** lists ore tiles and how much of the stone they make up (`3 6%, 4 3%`), each later one deeper; **Sand** and **Trees** (**Trunk Tile**, **Leaves Tile**) dress the surface, and **Bottom Tile** floors it. **Walls** names a second, non-solid tilemap to fill with the underground's back walls, which caves show instead of the sky. **Spawn** stands the player on the ground in the middle.
- **Blocks**, on the same tilemap, makes it something to dig and build with. **Items** says which item puts down which tile (`Dirt 1`), and mining a tile gives its item, unless **Drops** says otherwise (`0 Dirt` for grass, `6 nothing` for leaves). **Mine Time** and **Hardness** (`2 0.8, 3-4 1.2`) are how long blocks take to break; **Unbreakable** tiles never do. **Falling Tiles** drop while there's nothing under them, like sand. **Glowing Tiles** light the dark like a Light (**Glow Radius**, **Glow Color**), and **Underground** is the light the camera fades to below the ground, **Dark Depth** down. **Save As** keeps the world in the game's save data, so what's dug and built is still there next time. **Break Effect**, **Dig Sound**, **Break Sound** and **Place Sound** give it feel.
- **Block Builder**, on the player, digs with the left mouse button held on a block within **Reach** (faster with **Mine Speed**), the block cracking as it goes, and puts down its **Item** with the right button, next to another block and never on top of anyone. In first person it works on the block straight ahead, Minecraft style. What's dug goes into the player's **Inventory**.
- **Hotbar** shows the inventory in **Slots** along the bottom of the screen, each block drawn with its own tile, and picks one with the number keys, the mouse wheel or a click: that's what the builder puts down. Items keep their slot until they run out. **Colors** colors items that aren't blocks.
- **Crafting** lists **Recipes** like `Torch 4 = Wood + Coal`, opened and closed with its **Key** (C); click one to make it, lit up when there's enough. `@ Workbench` needs a workbench within **Station Reach**: a placed block of that item, or an entity with that name.

Tilemaps now only draw the tiles in view, so worlds can be big.

**Contraptions and wiring.** The **Contraption** template is a room to build machines in, Garry's Mod and Wiremod style: a lamp on a rope with a button, a lift of thrusters welded under a plank, a door held shut by logic until a crate is dropped on its plate, and a clock blinking a lamp and counting on a display. The pieces:
- **Sandbox Tools**, on the player, are tools to build with while the game runs, picked along the top of the screen, with the number keys or the mouse wheel. **Grab** carries things to the mouse (**Grab Strength**) and throws them when let go; right-click to freeze one in the air, or free a frozen one. **Spawn** puts down the prop picked from **Props** (prefab names; each gets a name of its own). **Weld** and **Rope** join the two things clicked; **Wire** joins an output of one to an input of another, asking which when there's a choice; **Remove** takes something away with its welds, ropes and wires. **Undo Key** (Z) undoes what was made, newest first. What's made is part of the scene, so **Keep Play Changes** brings a contraption back to the editor.
- **Body** has three new settings: **Frozen** holds it still where it is (others still bump into it), **Mass** says how heavy it is, and **Friction** stops it sliding along the ground.
- **Weld** sticks its body **To** another entity, by name: they move as one, sharing their speed by weight (so a balloon welded to a crate lifts it), and don't bump into each other. Welded to something without a body, it holds still. **Rope** ties it to another entity no further than **Length** apart (0 is as far as they start), from its **Offset** to the other's **Other Offset**, drawn in its **Color**.
- **Thruster** pushes its body (and what it's welded to) along its **Direction**, turned with the entity, with **Force** times its **Power** input; a particle emitter on it fires while it pushes.
- **Wire**, on the entity it drives, carries the **Output** of the entity named in **From** into this one's **Input**, drawn from one to the other (brighter while it's on, unless it isn't **Visible**). Every entity has the inputs **Power** (switching it on and off as a Switch does: movers, spawners, doors) and **Show**; outputs include a button's or switch's **On**, a gate's **Out**, a sensor's **Out** and **Count**, a body's **Speed** and **Height**, health's **Health** and a counter's **Count**. Several wires into one input add up.
- **Wire Gate** works out **Out** from its **A** and **B** inputs (an input with no wire keeps its value) as its **Kind** says: And, Or, Not, Xor, Nand, Nor, Add, Subtract, Multiply, Divide, Greater, Less, Equal, a Constant **Value**, a Toggle (flips each time A turns on), a Latch (A sets, B resets), a Delay of **Value** seconds, a Clock ticking every **Value** seconds, or a Counter. Chains of gates answer in the same frame, and each is labeled with what it does.
- **Wire Button** is pressed with Interact (E) with the mouse over it, or while touching it: a **Toggle**, or on only while held. **Wire Sensor** notices bodies inside its trigger (on its **Layers**). **Wire Lamp** lights from **Off Color** to **On Color** (and turns up a Light on it) with its **On** input, and **Wire Display** shows its **Value** input on its Text, or over itself.

**Multiplayer.** Any scene becomes a network game with a **Multiplayer** component, and the **Multiplayer** template is one to start from: a menu to type your name and a host's address into, and an arena for up to eight players with slimes, hearts, crates to hide behind and a scoreboard. One machine hosts and runs the game; everyone else's shows the host's world, sends the host their keys, and moves their own character itself, so it never waits on the connection. Cameras, the HUD, menus and music stay on each machine and follow that player's own character. The pieces:
- **Multiplayer**, on any entity in the scene: **Max Players**, the **Port** to host on, the **Player** each one plays (an entity in the scene, which the host plays and everyone else gets a copy of, or a prefab; empty for a strategy game, where players command teams), **Teams** for team games ("Blue, Red": players are shared out between them, appear at their team's spawns and can't hurt each other unless **Friendly Fire** is on; in a strategy game they're the factions players take, and a computer **Opponent** stands aside for a person playing its team), **Color Players** (each player's character in their own color, or their team's, and their shots too), **Score Kills** (a point for defeating another player or an enemy), **Send Rate**, **Smoothing** (how far behind the host everyone else's characters are shown, so they glide), **View Distance** (for a big world, like an MMO: only what's within it is sent to each player), **Remember Players** (a player who leaves gets their character back as it was when they join again, even in a later game) and **Exit Scene** (where to go when the host leaves).
- **Player Spawn** marks where players appear (for a **Team**, or anyone). **Name Tag** shows the name of the player something belongs to in its Text: put it on a child above the player. **Scoreboard** lists who's playing, with their score and ping, and how the game's going ("Hosting on 192.168.1.5:7777", "Joining...", why it ended), optionally only while a key like Tab is held. **Text Field** is a box to type into: a player's name, or the address to join.
- Buttons have **Host Game** (then go to their **Scene**), **Join Game** (the address typed into a Text Field, or with none, the first game found on the local network) and **Leave Game**. A pause menu doesn't stop a network game: the game goes on, and that player's keys just don't reach it until it's closed.
- **Networked** changes how one entity takes part: **Sync** shares it with everyone (the default for everything but cameras, HUDs, menus and music) or keeps it **Local** to each machine; **Movement** says whether a player's own machine moves their character (the default for characters with a movement component: instant to control) or the host does from their keys (nobody can cheat); **Always Sent** ignores the view distance; **Owner** is the player it belongs to, set by the game.
- What works for every player with no code: platformer, top-down and first-person movement (each player turns and walks on their own screen), weapons, turrets and attacks driven by their own keys and aim, health, damage and respawning (the host's spikes and checkpoints count for every player), pickups, doors, levers, buttons and dialogue (read on each player's own screen), enemies that chase or shoot at the nearest player, a strategy game's commander (each player selects and orders their own team; the orders go to the host), and a block world's digging, building, hotbar, crafting and sandbox tools (everyone in the same world, with their own tools and undo). Messages sent on the host reach everyone, so their HUD counters and sound players work too, and the host's sounds play for everyone.
- **Game > Play with 2 Players** (Ctrl+F5, or 3 or 4) plays as several players at once: the Game tab hosts and each other player gets a window of their own, joined over a pretend network inside the editor. Click a window to play as that player. **Game > Pretend a Slow Connection** adds lag and a few lost packets to see how the game feels on a bad connection.
- Exported games host and join from their own menus. On the command line, `--host [port]` hosts straight away, `--join <address>` joins, `--name <name>` sets the player's name, and `--server [port]` runs a dedicated server: the start scene, hosted for everyone and played by nobody, with no window.

**Painting sprites and tilesets.** **Art → New Sprite** opens the Sprite Painter, for pixel art. Paint with the **Pencil** (B), **Eraser** (E), **Fill** (G), **Line** (L), **Rectangle** (R) and **Ellipse** (O), or pick a color off the picture with the picker (I, or Alt-click); right-dragging erases. **Mirror** (M) paints both halves at once, and Ctrl+Z undoes a stroke at a time. Colors come from a palette (**Jam**, **Earth**, **Handheld** or **Grays**) or any `#RRGGBB`. Pick a starting point (**Creature**, **Ship**, **Item**, **Ground** or **Bricks**) for a fresh one made from scratch, and **Another** for a different one. A sprite is frames played in order: **Add Frame** copies the one you're on, **Onion skin** shows the frame before underneath, and the preview plays them at the **Speed** you set. **Art → New Tileset** (or the **Tileset** button) paints a grid of tiles instead, numbered from 0 as a tilemap's rows name them, with a preview that repeats the tile to check it joins up. **Save** puts it in `Sprites/` or `Tiles/` as a PNG that remembers its frames or tiles, so it opens again exactly as it was made. Every image field in the Inspector (a sprite's or tilemap's **Image**, particles, first-person weapons) shows a preview, a list of the project's pictures, and a **Paint** button that opens the painter on its picture (or a new one) and puts each save on it in one undo step: a sprite takes its **Columns** and **Rows** and an **Animation** to play several frames, a tilemap its tile grid, and a colored placeholder box loses its tint. **Art → Paint Selected** does the same for the selected sprite or tilemap. Double-click a picture in the Assets panel to paint over it; saved changes show in the scene and the running game straight away.

**Making sounds and music.** **Audio → New Sound** opens the Sound Maker. Pick a starting point (**Coin**, **Jump**, **Shoot**, **Explosion**, **Power Up**, **Hit**, **Blip** or **Random**) and it makes a fresh retro sound of that kind and plays it; click again for another. **Mutate** nudges every setting for "like that, but different", the **Wave** buttons change its tone, and the sliders shape it (its envelope, pitch slides, vibrato, a "ba-ding" change of pitch, repeats, a phaser and filters), playing it after every change. **Save** puts it in `Sounds/` as a WAV. **Audio → New Music** (or the **Music** button) writes a looping tune instead: pick a **Mood** (**Bright**, **Chill**, **Tense**, **Spooky** or **Heroic**: each has its own scale, chords, beat and accompaniment), then **New Tune** for another in the same mood, and set its **Tempo**, **Bars**, **Lead** sound and which **Parts** play (melody, bass, chords, drums). It's saved in `Music/`, and its end runs straight into its start, so it can play under a level forever. Sounds made here remember how they were made: double-click one in the Assets panel to play it, or right-click it and choose **Edit in Sound Maker** to tweak it and save over it (heard straight away, even in the running game). The Platformer, Top-down, Arcade, Strategy, Shooter, Sandbox, Contraption and Multiplayer templates come with their sounds and a theme tune made this way, already hooked up: jumps, coins, swings, hits, explosions, checkpoints, doors, switches, springs, menu buttons, orders, digging, building, crafting and the tools.

**Choosing sounds.** Every sound field in the Inspector (**Sound**, **Jump Sound**, **Music**, a script's `HitSound`) has three buttons: **Play** to hear it, a list of every sound in the project to pick from, and the Sound Maker, which opens on the field's sound if it was made there (or on a new one) and sets whatever it saves. Dragging a sound from the Assets panel onto a field sets it too. A field naming a file that isn't there turns red. Griff plays WAV, MP3 and OGG files.

**Music and sound players.** Add a **Background Music** to a scene and its **Music** starts with the scene, crossfading over **Fade** seconds from whatever was playing (the same music just carries on, so every level can name it). A **Sound Player** plays its **Sound** whenever its **Message** is sent (`Score` from a pickup, a switch's **On Message**, an enemy's **Death Message**) and, with **On Start**, when it appears; **Pitch Variation** changes its pitch a little at random each time, so a sound heard over and over doesn't grate, and **Positional** makes it come from where it is. A **Health** plays its **Hurt Sound** when damage lands and its **Death Sound** when it dies, and a **Button** plays its **Sound** when clicked.

**Sounds in the level.** Add a **Sound Source** to a waterfall, a fire or a machine and its **Sound** plays over and over, loudest near the middle of the screen and fading out by its **Range** (0 is heard everywhere at full volume: rain, wind), from the left or right speaker as it's to that side. Untick **Playing** to start it silent; a switch's **Targets** turn it on and off. Its **Pitch** can change as it plays: an engine that revs. Scripts can play a one-off sound where something is with `Game.PlaySoundAt`.

**Spawners and lifetimes.** A **Spawner** spawns its **Prefab** every **Interval** seconds where it stands, after a **Delay**, within a **Radius**, up to **Count** in all and **Max Alive** at once. A **Lifetime** destroys its entity after **Seconds**, fading it out over the last **Fade Out** seconds: bullets, effects, score popups.

**Typewriter text.** Add a **Typewriter** next to a **Text** and the text types itself out, **Speed** letters a second, pausing briefly at punctuation, with an optional blip **Sound**. Whenever a script changes the text, the new line types out from the start. It runs on real time, so it works in a paused game.

**Prefabs.** Select an entity and choose **Entity → Save as Prefab** to save it (with its children) to `Assets/Prefabs`. Drag a prefab into the Scene view to place it, or spawn it from a script. Save as Prefab works while playing too, to keep a live entity you've tweaked.

**Component catalog.** Next to **Add Component** (the quick list), the Inspector's **Catalog** button opens every component by category, searchable, with what it does, what it **Requires** on the same entity (checked against the selected entity, with what's missing marked), what it **Works well with**, what it's **Needed by**, and each property's kind, starting value and description. Names in the text are links to that component. **Add** puts it on the selected entity along with any requirement it's missing (a Patrol brings a Body and a BoxCollider), as one undo step. Scripts are listed too, described by their `///` comments.

In the **Inspector**, drag any number's label sideways to scrub it (Shift for fine steps, Ctrl for coarse). While the game is playing, typing in the Inspector doesn't reach the game; click the game to give it the keyboard back.

In the **Assets** panel:
- Drag an image into the Scene view to create a sprite.
- Drag any asset onto an Inspector text field to fill in its path.
- Double-click a scene to open it; right-click it to make it the start scene.

Games can switch scenes with `Game.LoadScene("Level2")`. **Game → Clear Save Data** forgets what the game saved while playing in the editor.

## Agents (MCP)

While the editor is open it runs a [Model Context Protocol](https://modelcontextprotocol.io) server on this computer, so an agent like Claude Code can build games with it: `claude mcp add --transport http griff http://127.0.0.1:7650/mcp` (or **Help → Copy MCP Setup Command**; **Help → MCP Server** turns it off; `GRIFF_MCP_PORT` picks another port). The server lists no features of its own. Its tools are read from what the editor already has, so everything the editor can do is there, and anything added later shows up by itself:

- **Every command** in the menus and toolbar is a tool, named by its id (`Entity_CreatePlatformerPlayer`, `Game_Play`, `Scene_Save`) and described by its documentation. Commands that act on the selection take an `entity` (or `select` several first: Delete, Duplicate, Cut and Copy act on all of them); a question a command asks (a scene's name, saving changes) comes back to the agent, which answers it with `answers`.
- **Every component**, Griff's and the project's scripts, can be added and set with `entity_create` and `entity_update` (values as scene files have them), described by `components_list` and `component_describe`. `scene_tree`, `entity_get` and `entity_find` look at the scene, the running game included. Each call is one undo step.
- **Every window, panel and editor service** (the Sprite Painter, the Sound Maker, the Controls window, the workspace, the Scene view's box selection and moving...) can be read and worked with `ui_roots`, `ui_inspect`, `ui_set` and `ui_invoke`.
- **Files**: `asset_write` writes scripts (compiled at once, errors reported), pictures and sounds; `asset_read` reads them, pictures as images.
- **Checking work**: `editor_screenshot` shows the game or the editor, `game_input` plays the running game with keys (and can pause it between moves), `editor_state` and `output_read` say what's going on.

It listens on the loopback address only and refuses requests from web pages.

## Scripting

Scripts are plain C# classes in your project's `Assets` folder. **Entity → New Script** creates one, attaches it to the selected entity and opens it. Common namespaces (`System`, `System.Collections`, `System.Numerics`, `Griff`, `Griff.Runtime`, `Griff.Scenes`) are imported automatically.

**The script editor.** Scripts open in Griff's own code editor, as tabs next to Scene and Game: from New Script, by double-clicking a `.cs` file in Assets, with the code button on a script component's header in the Inspector (**Edit Script**), or by double-clicking a script error in Output. It's C# with the compiler behind it, so it knows Griff's whole API and your other scripts, unsaved changes included:

- Colors by meaning: properties, methods, types, parameters and fields each have their own color. Code is set in JetBrains Mono, which ships with Griff (SIL Open Font License; see `Themes/Fonts`).
- Completion as you type, with each item's signature and documentation; signature help inside calls (Up and Down pick an overload); hover anything for its documentation, or for the compiler's message.
- Errors and warnings as you type: underlined where they are, counted in the header (click for the list), and in red in the line numbers.
- **Ctrl+S** saves, compiles and swaps the change into the scene, and into the game if it's playing. Playing saves open scripts first.
- **The component panel** beside the code (the **Component** button) shows the component the script declares: what it does, what it needs, and each property with its kind, starting value and description, plus its value on an entity in the scene that uses it (live while playing), editable right there. Click a property's name to go to it in the code; the property the caret is in lights up. New properties show up as soon as you type them, waiting for a save.
- F12 or Ctrl+click goes to a definition, Shift+F12 lists every use, F2 renames across all scripts, Shift+Alt+F formats, Ctrl+F finds and Ctrl+H replaces (with case, whole-word and regular-expression options), Ctrl+G goes to a line, Ctrl+/ comments lines, Ctrl+D duplicates them, Ctrl+Shift+L deletes them, Alt+Up and Alt+Down move them, and Ctrl+wheel zooms. Brackets and quotes close themselves, `}` lines up with its `{`, uses of the name at the caret are highlighted, blocks fold, and indent guides follow the nesting.
- Snippets: `prop` (a property for the Inspector), `update`, `start`, `trigger` and `coroutine` in a class; `if`, `for`, `foreach` and `while` in a method. Type the name and press Tab.

Scripts can still be edited in any other editor; Griff picks up the saved file either way.

```csharp
public class PlayerController : Behavior
{
    // Public properties are saved with the scene and editable in the Inspector.
    public float Speed { get; set; } = 7;
    public float JumpSpeed { get; set; } = 13;

    protected override void Update()
    {
        var body = GetComponent<Body>()!;
        var velocity = body.Velocity;
        velocity.X = Input.Horizontal * Speed;          // A/D or arrow keys
        if (body.IsGrounded && Input.WasPressed(Key.Space))
        {
            velocity.Y = JumpSpeed;
            Audio.Play("Sounds/jump.wav");
        }
        body.Velocity = velocity;
    }

    protected override void OnTriggerEnter(Entity other)
    {
        if (other.Name == "Coin")
            Game.Destroy(other);
    }
}
```

Describe a script for the component catalog with a `///` summary over the class and its properties, and say how it fits with other components using attributes:

```csharp
/// <summary>Walks toward the player and explodes next to them.</summary>
[Category("Enemies")]                                  // where the catalog lists it (default: Scripts)
[Requires(typeof(Body), Note = "moves by velocity")]   // the catalog adds a Body along with it
[Requires(typeof(Collider))]                           // a base type: any kind of collider will do
[WorksWith(typeof(Health), Note = "so it can be shot")]
public class Creeper : Behavior
{
    /// <summary>How close it gets before exploding, in world units.</summary>
    public float FuseRange { get; set; } = 1.5f;
}
```

A few more things scripts often do:

```csharp
// Spawn a prefab (Assets/Prefabs/Bullet.prefab) where the player is.
var bullet = Game.Spawn("Bullet", new Vector2(Transform.Position.X, Transform.Position.Y));

// Do something later, or repeatedly. Timers belong to this entity and stop if it's destroyed.
After(0.5f, () => Game.Destroy(bullet));
Every(2f, () => Game.Spawn("Enemy", new Vector2(10, 0)));

// Is there ground just below? Ignore our own collider.
var below = Game.Physics.Raycast(Game.Scene, new Vector2(Transform.Position.X, Transform.Position.Y), -Vector2.UnitY, 0.6f, ignore: Entity);

// Juice: a burst of particles and a camera kick.
GetComponent<ParticleEmitter>()?.Burst(20);
Game.ShakeCamera(0.3f, 0.2f);

// Tweens: pop in, glide somewhere, then fade out and disappear.
Transform.Scale = new Vector3(0, 0, 1);
ScaleTo(Vector2.One, 0.4f, Ease.OutBack);
MoveTo(new Vector2(4, 2), 1f).Then(() => FadeTo(0, 0.3f).Then(() => Game.Destroy(Entity!)));

// Find things. Every copy of a prefab shares its name; scripts are found by type.
foreach (var enemy in Game.FindAll<Enemy>())
    enemy.Hit();
Game.Find<ScoreCounter>()?.Add(1);

// Random spawn points, loot rolls and variety.
if (Rand.Chance(0.25f))
    Game.Spawn(Rand.Pick(["Coin", "Gem"]), Rand.InsideCircle(3));

// Sequences that take time: a coroutine waits with `yield return` and picks up where it left off.
StartCoroutine(Waves());

IEnumerator Waves()
{
    for (var wave = 1; wave <= 3; wave++)
    {
        for (var i = 0; i < wave * 4; i++)
        {
            Game.Spawn("Enemy", Rand.InsideCircle(6));
            yield return Wait(0.3f);
        }
        yield return WaitUntil(() => Game.FindAll<Enemy>().Count == 0);
    }
    Game.LoadScene("Win", fade: 0.5f);
}

// Messages: tell whoever's interested, without knowing who they are. Listen in Start; listeners stop by themselves
// when their entity is destroyed or the script reloads.
Game.Send("Score", 10);
On<int>("Score", points => Total += points);
On("PlayerDied", () => Game.ReloadScene(fade: 0.5f));

// Named animation clips on one sprite sheet (Clips: "Idle 0-3, Run 4-9 @12, Jump 10, Die 11-15 once").
GetComponent<Animation>()!.Play(body.IsGrounded ? (moving ? "Run" : "Idle") : "Jump");

// Chase the player around walls: the waypoints to head for, one after another.
var path = Game.Find("Level")!.GetComponent<Tilemap>()!.FindPath(Transform.WorldPosition, player.Transform.WorldPosition);
if (path.Count > 0)
    body.Velocity = Vector2.Normalize(path[0] - new Vector2(Transform.Position.X, Transform.Position.Y)) * Speed;

// See what the code is thinking: shapes drawn over the game this frame (or for a few seconds).
Debug.Path(path, Color.Yellow);
Debug.Circle(Transform.Position, SightRange, Color.Red);
Debug.Text(Transform.Position + new Vector3(0, 1, 0), state);

// Line of sight that sees through other enemies: only walls (the Default layer) and the player block it.
var seen = Game.Physics.Raycast(Game.Scene, eye, toPlayer, 10, ignore: Entity, layers: "Default, Player");

// Game flow: start over after dying, keep the music playing from scene to scene, quit from a menu.
Game.ReloadScene(fade: 0.4f);
Game.KeepBetweenScenes(Entity!);
Game.Quit();

// Named input actions: Jump, Fire, Dash, Interact and Pause work on keyboard, mouse and gamepad from the start.
if (Input.WasPressed("Jump") && body.IsGrounded)
    velocity.Y = JumpSpeed;
Input.Bind("Dash", Key.LeftShift, Key.K, GamepadButton.B);   // change one, or add your own

// A jump pressed a moment before landing still jumps when you land (each press counts once), and one pressed a moment
// after running off a ledge still jumps (coyote time). Letting go early makes a short hop.
if (body.WasGrounded(0.1f) && Input.WasPressed("Jump", buffer: 0.12f))
    velocity.Y = JumpSpeed;
if (Input.WasReleased("Jump") && velocity.Y > 0)
    velocity.Y *= 0.5f;

// Move without going faster diagonally; aim with the right stick, or the mouse; shake the controller.
body.Velocity = Input.Move * Speed;
var aim = Input.AimFrom(Transform.Position);
if (Input.IsDown("Fire") && aim != Vector2.Zero)
    Shoot(aim);
Input.Rumble(0.6f, 0.25f);

// A controls screen: press a key for Jump. The player's choice is saved and comes back next time.
if (waitingForKey && Input.AnyPressed is { } pressed)
{
    Input.Rebind("Jump", pressed);
    waitingForKey = false;
}
label.Value = $"Jump: {string.Join(" / ", Input.BindingsOf("Jump"))}";   // "Jump: Space / Gamepad A"

// Type your name for the high score table.
PlayerName = Input.TypeInto(PlayerName, maxLength: 12);

// Hurt things, and make hits feel like hits.
Game.Find("Boss")!.GetComponent<Health>()!.Damage(2, source: Entity);
Flash(Color.Red);        // this entity's sprites, for a moment
Game.HitStop(0.08f);     // freeze the game for a split second
Game.FlashScreen(Color.Red with { A = 0.3f }, 0.2f);   // the whole screen
Game.SlowMotion(0.25f, 1);   // the last enemy falls: a second at quarter speed, easing back

// Scores without a counter of your own: add to the Counter, or read it.
Game.Find<Counter>()!.Add(100);
var score = Game.Find<Counter>()!.Value;

// Dialogue: change the text and the Typewriter types it out. A key press finishes the line, then moves on.
if (Input.WasPressed("Interact"))
{
    if (!typewriter.IsDone)
        typewriter.Skip();
    else
        text.Value = lines[++line];
}

// An options menu (sliders and toggles do this with no code). It's remembered next time.
Audio.MusicVolume = 0.5f;
Audio.Muted = !Audio.Muted;
Game.Fullscreen = true;

// A panel opens: pick its first button for the keyboard and gamepad.
Game.Focus(Game.Find("Retry")!.GetComponent<Button>());

// Panels: open a shop from a shopkeeper, close the top one, and see what's open.
Game.OpenPanel("Shop");
Game.ClosePanel();
if (Game.IsPaused) return;   // a pause menu is open
GetComponent<Panel>()!.Opened += _ => Audio.Play("Sounds/open.wav");

// Show the player what to press, on the keyboard or the controller they're using.
hint.Value = $"Press {Input.PromptFor("Jump")} to jump";   // or just "Press {Jump} to jump"
buy.Interactable = coins >= 10;
Game.ListenForBinding("Dash");   // a rebind button of your own

// Wall jumps: WallSide is -1 or 1 while a wall stops the body (it has to be pushing into it).
if (!body.IsGrounded && body.WallSide != 0 && Input.WasPressed("Jump"))
    body.Velocity = new Vector2(-body.WallSide * 6, JumpSpeed);

// Levels: a cutscene line when the boss appears, a fade to cover a cut, enemies that just spotted you.
Game.StartDialogue(Game.Find("Boss")!.GetComponent<Dialogue>()!);
Game.FadeThrough(0.4f, () => player.Transform.Position = new Vector3(40, 2, 0));
GetComponent<Chase>()!.Spotted += _ => Audio.Play("Sounds/alert.wav");

// Puzzles: pull a lever from a cutscene, see what the player carries, react to a plate.
Game.Find("Gate Lever")!.GetComponent<Switch>()!.IsOn = true;
if (player.GetComponent<Inventory>()?.Has("Key") == true)
    Debug.Text(Transform.Position, "I have the key");
GetComponent<Switch>()!.Changed += on => Game.ShakeCamera(on ? 0.2f : 0.1f);
Game.Power(Game.Find("Fan")!, on: false);   // what a switch would do: stop its movers, spawners, particles...

// Players: tune the built-in movement from a script, or react to it.
var player = GetComponent<PlatformerMovement>()!;
player.AirJumps = hasBoots ? 1 : 0;
player.Landed += speed => Game.ShakeCamera(speed / 100);
if (player.IsClimbing || player.IsDashing) return;
player.Launch();                             // thrown by an explosion: air jumps and dash come back
body.DropThrough();                          // fall through the one-way platform underneath

// Atmosphere: is the player in the shadows? A sound from over there, music for the boss, a light that dies.
// A storm rolls in, and the color drains as health runs low.
if (Game.LightAt(player) < 0.3f) return;   // the guard can't see them
Game.PlaySoundAt("Sounds/creak.wav", new Vector2(Transform.Position.X, Transform.Position.Y));
Audio.CrossfadeMusic("Music/boss.mp3", seconds: 2);
GetComponent<Light>()!.Intensity = 0;
Game.Find<Weather>()!.Amount = 1;
Game.Find<ScreenEffects>()!.Saturation = health.Current / health.Max;

// Rooms and attacks: react to the camera moving on, swing from a cutscene, add to a hit.
GetComponent<Room>()!.Entered += _ => Game.Find("Boss")!.IsEnabled = true;
GetComponent<Attack>()!.Swing();
GetComponent<Attack>()!.Hit += enemy => Game.ShakeCamera(0.15f);

// Remember things between runs.
if (Score > Game.SaveData.GetInt("best"))
    Game.SaveData.Set("best", Score);
```

`Rand` gives random numbers for gameplay: `Range`, `Chance`, `Pick`, `Sign`, `Direction`, `InsideCircle`, `Color`, and `Seed` to make a run repeatable.

What a behavior can use:
- **Lifecycle:** `Start`, `Update`, `OnCollision`, `OnTriggerEnter`, `OnTriggerExit`, `OnClick` (the entity's button was clicked), `OnDamage` (its Health was hurt: how much, and by what), `OnDeath` (its Health reached zero, just before it's destroyed), `OnPickup` (it collected a Pickup), `OnDestroy`.
- **Services:** `Transform`, `Time` (`Delta`, `Total`, `Scale`), `Input` (`IsDown`, `WasPressed`, `WasReleased`, `Horizontal`, `Vertical`, `Move`, `MouseWorld`, `Scroll` (mouse wheel notches this frame); keyboard, mouse and Xbox-style gamepads, with `LeftStick`, `RightStick`, `LeftTrigger` and `RightTrigger` (also buttons, `GamepadButton.RightTrigger`, when pulled past halfway), `IsGamepadConnected`, `UsingGamepad` (for showing "Press A" rather than "Press Space") and `Rumble`; `AimFrom(position)`, the right stick or towards the mouse; named actions: `IsDown("Fire")`, `WasPressed("Jump")`, `WasPressed("Jump", buffer: 0.12f)` (a press in the last moment that hasn't been used yet), `WasReleased`, with `Bind` to set an action's keys and buttons or add a new one, and `BindingsOf` to show them. `Rebind` is for the player's own choice from a controls screen: it's saved, comes back next time and wins over `Bind`; `ResetBindings` forgets it, and `AnyPressed` is whatever was just pressed. Actions are case-insensitive; a misspelled one says which exist. `TypedText` and `TypeInto` are for typing names; `PromptFor("Jump")` is what to show for an action on the device in use, "Space" or "A", and `InputPrompts.Name` names any key or button), `Audio` (`Play` (optionally with a `pan` from -1, left, to 1, right, and a `pitch`: 2 is an octave up), `PlayMusic`, `CrossfadeMusic`, `FadeOutMusic`, `StopMusic`, `Loop` (a sound that repeats until stopped, whose `Volume`, `Pan` and `Pitch` can change as it plays), and `Volume`, `MusicVolume`, `SoundVolume` and `Muted` for an options menu; the same sound started several times in one frame plays once) and `Debug` (`Line`, `Ray`, `Box`, `Circle`, `Point`, `Path`, `Text`: drawn over the game for one frame, or for `seconds:`, with lines the same thickness however far the camera zooms; `Debug.Enabled = false` hides them all).
- **Multiplayer:** `Game.Net` hosts and joins network games and says who's playing. `Host()`, `HostServer()` (a dedicated server), `Join("192.168.1.5")` (or `Join("")` for the first game on the local network) and `Leave()`; `Role`, `IsOnline`, `IsHost`, `IsClient`, `IsConnecting` and `Status`; `Players`, `LocalPlayer` (each a `NetPlayer` with `Id`, `Name`, `Team`, `Color`, `Score`, `Ping`, `IsHost`, `IsLocal` and their `Avatar`), `PlayerJoined`, `PlayerLeft` and `Ended` events, and the messages "PlayerJoined" and "PlayerLeft". `Net.Send("Chat", "gg")` reaches every machine as a message (data can be a number, text, a vector, a color, an entity, a player or a list of them), with `Net.Sender` saying who sent it; `SendToHost` and `SendTo(player, ...)` reach one. `OwnerOf(entity)` and `IsMine(entity)` say whose something is, `Respawn(player)`, `AddScore`, `SetTeam` and `Kick` are the host's, `PlayerName` is remembered, and `FakeLag` pretends a bad connection. Scripts run on the host; their `Input` is the keys of whoever plays their entity (`Game.InputOf(entity)` for any entity), so a script on a player's character answers to that player.
- **Messages:** `Game.Send("Name")` or `Game.Send("Name", data)` reaches every listener straight away and returns how many heard it. `On("Name", () => ...)` and `On<int>("Name", value => ...)` listen until the entity is destroyed or the script is reloaded (like coroutines, so listen in `Start`); they return a listener you can `Cancel()`, and `StopListening()` stops all of a behavior's. Listeners in a scene you leave stop too, unless their entity is kept with `KeepBetweenScenes`. `Game.On` makes one that doesn't belong to a behavior.
- **Timers:** `After(seconds, action)` and `Every(seconds, action)` return a timer you can `Cancel()`. They run on game time, so they pause and slow down with it. `Game.After` and `Game.Every` make timers that don't belong to an entity.
- **Coroutines:** `StartCoroutine(SomeMethod())` runs an `IEnumerator` method straight away up to its first `yield return`, then picks it up again once what it yielded is done: `null` (the next frame), `Wait(seconds)` (or just a number), `WaitUntil(() => ...)`, `WaitWhile(() => ...)`, a tween, a timer, another coroutine, or another `IEnumerator` method (run to the end first, like calling it). They run on game time like timers and return a coroutine you can `Stop()`; `StopCoroutines()` stops all of a behavior's. They stop when the entity is destroyed and when the script is reloaded, and since `Start` then runs again on the new version, a coroutine started in `Start` restarts running your new code. `Game.StartCoroutine` makes one that doesn't belong to a behavior.
- **Tweens:** `MoveTo`, `ScaleTo`, `RotateTo` and `FadeTo` (sprite, text and tilemap alpha) animate this entity. `Tween(seconds, t => ...)` animates anything. Each takes an optional curve from `Ease` (`Linear`, `InQuad`, `OutQuad` (the default), `InOutQuad`, `InCubic`, `OutCubic`, `InOutCubic`, `InOutSine`, `InBack`, `OutBack`, `OutElastic`, `OutBounce`) and returns a tween you can `Cancel()` or chain with `.Then(...)`. A new `MoveTo` takes over from an unfinished one. Like timers, tweens run on game time and stop if their entity is destroyed; `Game.MoveTo(entity, ...)` and friends work on any entity.
- **Game:** `Spawn` (an entity, or a prefab by name), `Destroy`, `Find` and `FindAll` (by name, or `Find<T>()` and `FindAll<T>()` by component type), `LoadScene` and `ReloadScene` (optionally with a `fade:` in seconds and a `color:`; fades use real time, so they work from a paused menu, and `IsChangingScene` is true until the switch), `KeepBetweenScenes` (carry an entity into the next scenes, still running; returning to its original scene doesn't duplicate it), `Quit`, `ShakeCamera`, `HitStop` (freeze game time for a moment of real time; `Time.IsFrozen` is true meanwhile), `SlowMotion` (game time at a fraction of normal speed for a moment of real time, easing back; on top of `Time.Scale`, and `Time.SlowMotionScale` says how much), `Flash` (an entity's sprites and its children's), `FlashScreen` (the whole view, fading out on real time), `Fullscreen` (remembered, like the volumes), `Focus` and `Focused` (the button or slider picked for the keyboard and gamepad), `OpenPanel`, `ClosePanel`, `TopPanel`, `OpenPanels` and `IsPaused` (see Pause menus and panels), `ListenForBinding` and `Rebinding` (see Controls screens), `FadeThrough` (fade out, call something while the screen is covered, fade back in), `LightAt` (how lit a place or an entity is, 0 to 1), `PlaySoundAt` (a sound from somewhere on or off screen), `StartDialogue`, `AdvanceDialogue` and `Talking` (the dialogue being said), `SaveData` (`GetInt`, `GetFloat`, `GetBool`, `GetString`, `Set`, `Delete`, `Clear`) and `Physics` (`Raycast`, `OverlapPoint`, `OverlapBox`, `OverlapCircle`; each takes `layers:` to only find colliders on those layers).

Built-in components:
- `Transform`, `Camera` (with `Shake`), `Sprite` (single images or sprite sheets, optionally nine-sliced with **Slice**) and `Text` (with `Align` and a `Font`: a `.ttf`/`.otf` file in Assets, which ships with the game, or an installed font's name);
- `Animation`, which plays a range of sprite-sheet frames, or named **Clips** (`Idle 0-3, Run 4-9 @12, Die 11-15 once`: a name, frames, and optionally a speed and `once`) switched with `Play("Run")`, which carries on if that clip is already playing. Its **Clip** picks the one the game starts with;
- `Tilemap`, a painted grid of tiles from one tileset (`GetTile` and `SetTile` from scripts), with background and one-way tiles. `FindPath(from, to)` finds the shortest way between two world positions around its solid tiles (background and one-way tiles can be walked through), as waypoints at the corners, moving diagonally but never cutting a wall's corner (pass `diagonal: false` for four directions);
- `ParticleEmitter`, for continuous effects or one-shot bursts (`Burst`; `BurstOnStart` with `DestroyWhenFinished` makes a self-cleaning effect prefab);
- `Button`, a clickable area that lightens under the mouse and darkens while held, with a no-code **Action** (and **Fade**), a **Shortcut** key, `OnClick` for behaviors on its entity and a `Clicked` event for anyone else, and `IsFocused` while the keyboard or gamepad has picked it. Put the label `Text` on the button's own entity so it's shaded with it;
- `Slider` (`Value` between `Min` and `Max`, `Snap`, a `Changed` event) and `Toggle` (`IsOn`, a `Changed` event; it needs a `Button` on its entity), each with a **Setting** or **Save As**; see Options above;
- `Panel` (**Open**, **Pause Game**, **Open On**, **Close On Back**, **First**, `IsTop`, and `Opened` and `Closed` events) and `Choice` (`Index`, `Value`, `Step`, a `Changed` event; it needs a `Button` on its entity); see Pause menus and panels, and Controls screens above. Buttons have `Interactable`, `NeedsSaved`, `IsUsable` and `IsListening`, and `Text` has `Shown`, its value with button prompts filled in;
- `Bar` (`Value`, `Max`, `Fraction` and **Track**); see HUD bars above;
- `Pickup` (`IsCollected` and a `Collected` event) and `Counter` (`Value`, `Best`, `Add` and a `Changed` event); see Pickups and scores above;
- `Follow`, which moves its entity after a **Target**, smoothly, with an offset, dead zone, look-ahead and an area to **Stay Inside** (see Cameras above). Scripts can set `TargetEntity` to follow something directly. `Camera` has **Sort By Y** for top-down games;
- `Parallax`, which scrolls a background layer at a fraction of the world's speed and can repeat its sprite across the view;
- `ScreenAnchor`, which pins its entity to one of nine points on the screen plus an **Offset**, for HUDs and menus. It's applied while the game runs, so the Scene view shows the entity where its transform puts it;
- `Health` (`Damage`, `Heal`, `Kill`, `IsDead`, `IsInvincible`, **Respawn**, and `Damaged`, `Died` and `Respawned` events) and `DamageOnTouch` (with **Kill**); see Health and hazards and Checkpoints above. `Sprite` has `Flash` and `Blink` for scripts to use themselves;
- `Spawner` (with an `EntitySpawned` event, `Spawned` and `Alive`) and `Lifetime`;
- `Typewriter` (`IsDone`, `Skip`, `Restart` and a `Finished` event);
- `Mover` (`Path`, `OffsetAt`, `TripSeconds`, `IsFinished`), `Patrol` (`Turn` and a `Turned` event), `Chase` (`IsChasing`, `TargetEntity` and a `Spotted` event) and `Shooter` (a `Fired` event, to give each shot something extra); see Moving platforms, Enemies and Turrets above;
- `Switch` (`IsOn`, which scripts can set, and a `Changed` event), `Inventory` (`Count`, `Has`, `Add`, `Take`, `Clear` and a `Changed` event) and `Trail` (`Copies`); see Switches, Keys and inventory, and Trails above. Movers, spawners and shooters have `Running` and doors have `Locked`, which switches set and scripts can too. A tilemap's `ShownTile` is the frame an animated tile is showing;
- `Checkpoint` (`Active` and a `Reached` event), `Door` (a `Used` event) and `Dialogue` (`IsTalking`, `Line`, `IsDone`, and `LineShown` and `Finished` events); see Checkpoints, Doors and Dialogue above. `Follow` has `Snap`, to jump straight to its target after a teleport of your own;
- `PlatformerMovement` (`Facing`, `IsClimbing`, `IsDashing`, `IsWallSliding`, `JumpsLeft`, `JumpSpeedFor(gravity)`, `Launch`, and `Jumped`, `Landed` and `Dashed` events) and `TopDownMovement` (`Facing`, `IsDashing` and a `Dashed` event); see Players with no code above. `Animation` has `HasClip`;
- `Ladder`, `Pushable`, `Bouncer` (a `Bounced` event) and `Crumble` (`Crumbled`, `IsShaking`, and `Fell` and `Returned` events); see Ladders, crates, springs and crumbling ledges above. A tilemap's `IsLadder` and `LadderAt` find its ladder tiles, and `ShownTile(x, y, tile)` is the piece an autotile shows;
- `Light` (`CurrentIntensity`, its flicker included, `WorldCenter` and **Shadows**), `Weather` and `ScreenEffects` (see Shadows, Weather and Screen effects above), `Room` (`Area`, `IsCurrent` and an `Entered` event) and `SoundSource` (`Loudness`); the camera has **Ambient**, `CurrentAmbient` (a room's, as it fades in) and `IsDark`, and **Transition**, and `Follow` has `CurrentRoom` and `IsChangingRoom`. See Rooms, Lights and darkness, Scene transitions and Sounds in the level above;
- `Unit` (`MoveTo`, `AttackMoveTo`, `Attack`, `Stop`, `Hold`, `Order`, `Destination`, `Target`, `IsSelected` and an `Arrived` event), `Worker` (`Gather`, `Carrying`, `Load`, `Source`), `Building` (`IsBuilt`, `Rally`, `IsSelected` and a `Finished` event), `Producer` (`Queue`, `Progress`, `Options` and a `Trained` event), `Fighter` (`Target` and a `Swung` event) and `Faction` (`Amount`, `Add`, `CanAfford`, `Spend`, `Refund`, `Lacking`, `IsDefeated` and a `Defeated` event); `Costs.Parse` reads a cost. `Game.Selected` and `Game.Select` are the player's selection, `Game.Train`, `Game.CancelTraining` and `Game.PlaceBuilding` do what the command card does (saying why not when they can't), `Game.FactionOf`, `Game.SupplyUsed` and `Game.SupplyProvided` look up a team, `Game.IsVisible` asks the fog of war, and `Game.LookAt` points the camera; see Real-time strategy above;
- `Multiplayer`, `Networked`, `PlayerSpawn`, `NameTag`, `Scoreboard` and `TextField` (`Value`, `IsFocused`); see Multiplayer above. `Scene.FindNamed` finds an entity by name, this machine's player's own first;
- `FirstPersonMovement` (`Facing`, `Pitch` and `Stride`) and `Weapon` (`IsCoolingDown`, and `Fired` and `Hit` events); `Game.FirstPersonView`, `Game.FirstPersonViewer`, `Game.CurrentWeapon`, `Game.Equip` and `Game.Fire` (a shot without ammo, for cutscenes), and `Input.MouseDelta`, `Input.WantsMouseLock` and `Input.IsMouseLocked` for mouse look of your own; see First-person shooters above;
- `Attack` (`Swing`, `IsAttacking`, `IsHitting`, and `Swung` and `Hit` events); see Swords and close-up attacks above. Tilemaps have `SlopeOf`, `SlopeCells` and `Physics.SlopeShapes`;
- `BoxCollider`, `CircleCollider` and `Body`, for physics. Boxes and circles collide in any combination. `GetComponent<Collider>()` finds either kind. A body's `Bounciness` (0 to 1) makes it bounce off what it hits, settling once the bounces get small. `Ground` is what it's standing on; when that moves (a platform moved by a script or a tween) the body rides along with it. `WasGrounded(0.1f)` is true while grounded and for that long after walking off an edge (but not after jumping), `AirTime` is how long it's been off the ground, and `WallSide` is -1 or 1 while a wall on that side stops it (for wall jumps), and `DropThrough()` falls through one-way platforms for a moment (`IsDroppingThrough`; `Game.Physics.OnlyOneWayBelow` says whether there's anything else underneath). Give colliders a **Layer** (`Player`, `Enemy`, `Bullet`; empty is `Default`) and list what they pass through in **Ignore Layers**, e.g. `Player, Bullet` on a bullet: ignored pairs don't block each other, collide or trigger, and only one side has to list the other. Tilemaps have both too.

Saving a script while the game is playing applies it immediately. Public properties are kept, and `Start` runs again on the new version.

Rewinding restores everything a scene file holds. Things that only exist while running (timers, coroutines, message listeners, tweens, live particles, camera shake, a scene fade, debug shapes, hit flashes, screen flashes, slow motion, invincibility, hit stop, a typewriter's progress, spawner and lifetime clocks, which control is picked, a bar's trail, a chaser's sense of where the target is, a conversation, a respawn countdown, a rebind button waiting for a key, a dash or a climb under way, a player's air jumps, a crumbling ledge's shake or its countdown back, a swing under way, a light's flicker, weather's drops and the next lightning, the camera's slide into a room and the room's light fading in, sound sources' loops, which sound players and background music have started, and in a strategy game the selection, orders, paths, training queues, arrows in flight and what the fog has uncovered) start fresh from the moment you resume, the same way private fields do. Movers, checkpoints and panels keep their place: a mover's **Time**, a checkpoint's **Active** and a panel's **Open** are saved, so rewinding into a pause keeps the pause menu up. So do puzzles: switches, what they've switched (**Running**, **Locked**), keys used up and inventories are all in the scene, while trail copies and the animated tiles' clock start fresh. A ledge that has crumbled stays gone (its **Crumbled** is saved) and comes back in its own time.

## Architecture

```
src/
  Griff                    Engine core: scenes, serialization, editing/undo, runtime, physics,
                           render lists, audio (mixer, sound and music synths), networking
                           (UDP with reliable and unreliable messages, and multiplayer on top),
                           projects and templates. No UI dependencies.
  Griff.Scripting          Roslyn script compiler, hot reload, game exporter
  Griff.Graphics           OpenGL 3.3 sprite and text renderer
  Griff.Platform.Windows   WGL context, WPF game surface, input, sound output and decoding, text rasterizing
  Griff.Editor             WPF editor: modules, commands, panels, design system (Themes/)
  Griff.Player             Runs exported games
tests/
  Griff.Tests              Unit and integration tests, including playing every template
```

The engine lives in `Griff` so that the editor, the player and the tests all use one model. The editor itself is built from modules: each feature registers its commands, panels and menus, and the shell assembles them. See `src/Griff.Editor/Themes/README.md` for the design system.

## Building

Requires the .NET 10 SDK on Windows.

```
dotnet build
dotnet test
dotnet run --project src/Griff.Editor
```

Exported games are self-contained: they include the .NET runtime and run on any 64-bit Windows PC without installing anything (expect a zipped download of about 60 MB).

## Status

Griff is young. Known gaps:
- No web export yet.
- Physics uses axis-aligned boxes and circles. Rotation is ignored, slopes are tilemap tiles at 45 degrees only, and circle bodies slide rather than roll.
- Shadows are hard-edged, and come from colliders and tiles rather than sprites' shapes.
- Tilemaps use one tileset of up to 62 tiles.
- Panels are resizable but not dockable.
- Several entities can be selected, but the Inspector edits only the last one picked, and dragging rows in the Hierarchy moves one at a time.
- Entities can't be locked in the Scene view yet, so a drag that starts on a big backdrop sprite moves it rather than drawing a selection box.
- Strategy games' units only walk (no flying or water), and the fog of war is drawn in squares.
- Multiplayer connects directly: to play over the internet, the host forwards their port (7777 by default) on their router; there's no matchmaking, relay or lobby server yet. Players' own machines move their own characters, which suits friends better than strangers (pick **Host** movement where cheating matters), and shots are judged where the host sees things, without rewinding for lag. Rewinding in the editor works with one player, and a network game can't hit-stop or slow down (it's everyone's).
- Block worlds are side-on (Terraria style); in first person, digging works on the flat map's walls. Water and lava don't flow, and light from glowing blocks casts no shadows.
- Contraptions don't turn: welded things move together but don't rotate, wheels don't roll, and ropes are straight lines that keep things within reach rather than swinging, sagging ropes.
- First-person games are drawn the classic way, from a flat map: walls are all one height, floors and ceilings are flat colors (no textures, stairs or rooms above rooms), and there's no aiming up or down at things.
