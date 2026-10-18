<!--
  What changed in each Griff release, in plain words, newest first. The Releases page shows this file.
  "Unreleased" collects the changes since the last release; tools/changelog.ps1 -Version <x> turns it into that
  version's entry (dated today) and starts a new empty Unreleased. Format: "## Unreleased" or "## <version> - <date>",
  then "- " bullets; `code` is shown as code and **bold** as bold.
-->

## Unreleased

- A saved game keeps the game's model, not just its scene: scripts' private fields and the plain objects, lists and dictionaries they hold (shared ones shared, cycles closed), the entities and components they point to, and scripts' systems, read back by name so the next build of the scripts loads them. A coroutine isn't saved: each script hears `OnLoaded` once its fields are back, to start its work again. `[NotSaved]` leaves a field out, `ISavesState` lets a script save its own way, and an `ISaveUpgrader` can change the saved fields too.
- Conveyors carry several items to a cell: with a **Spacing** (`0.25` is four to a cell) items glide on smoothly a set distance apart, round corners, and pile up packed at a full end or before a full machine, still the same at 16x and after a rewind. An **under** cell lets a crossing line pass beneath a belt, neither waiting for the other; a full closed loop keeps going round; a splitter's **Filters** send named items one way and the rest another; **Moving Arrows** and **Belt Tiles** move the belts' look with the items; and an Out port's **Holds** is a machine's tray (`Give` returns false and the port is **Full** once it holds that many). Scenes from before keep one item a cell.
- Juice with no code, each off until it's set: a **Button**'s **Hover Grow**, **Press Shrink** and **Pop Time** make it pop under the mouse and as it's pressed, easing its size and shade; a **Text** showing an **Output** with a **Count Time** counts up or down to a new number; a **Layout** with **Slide** slides its children to new places; a **Camera** kicks one way and back (**Kick By**, **Kick Time**, its **Kick** action, `Game.KickCamera`); and a **TweenPlayer** plays a little scale, move, turn and fade of how something is drawn, eased and looped if wanted, as it appears or on a message: a card popping in, a stamp landing, a coin bobbing.
- Lists the player puts in order: a **Layout** with **Reorderable** ticked lets a row be dragged up or down (or about a grid) with the mouse or a finger, the other rows sliding out of the way and the list scrolling near its end, or lifted with keys or a controller (**Grab Action**) and moved with the arrows. A plain click still clicks the row's Button, Esc or a drop off the list puts it back, and each move raises `Reordered(row, from, to)` and sends a **Reorder Message**; `Move` and `Layout.MoveIn` serve scripts with ▲ ▼ buttons or a list of their own, and **Save Order As** keeps the order in the save data.
- An arrow on the HUD pointing the way to something off the screen: **Entity → UI → Edge Pointer** (an **EdgePointer**) sits on the screen's edge toward its **Target**, turned to point at it and sliding along the edge as the camera pans and zooms, hides once the target is in view (or sits over it), shows how far off it is, and glides the camera there when it's clicked.
- A **Selectable**'s area turns with its entity, so a parked truck turned 90 degrees is clicked, boxed and marked as it lies, and a Button on a Selectable or inside one (a worker's own button) no longer stops **Selection Controls** selecting it: the click selects and the button still fires.
- The editor writes a line to the Windows Application log as it starts (with its version) and as it closes, saying why (the Exit command, an agent's App.Exit, its window closed, restarting into an update), and an error from outside the UI as it ends: an editor that went with no closing line was ended from outside.
- Fixed: a camera's **Edge Scroll** no longer pans while the mouse rests on the HUD at the screen's edge (a button row along the bottom, an open panel), as the wheel and drags already didn't; beside it, the edge still pans.

## 0.1.0-alpha.19 - 2026-10-18

- Lines with corners laid by dragging, for belts, pipes and rails: **Line Tiles** on a tilemap (a straight piece and a corner, each drawn once, optional end pieces) are laid turned to follow the line, by the Scene view's new **Line** tool (L; Z turns a single cell or swaps the corner) and in the game by a **Line Tool**: the player presses, drags and lets go, a ghost shows the line green where it fits and red on walls, past **Max Cells** or where a script's `CanLay` says no, and letting go lays belts on its **Conveyor** and tiles on its **Tiles** map and raises `Placed`. The line goes the way the drag started and turns one corner by itself; R (the new **Rotate** action) turns a single cell or swaps the corner.
- `project.griff` changed outside the editor (a text editor, a pull, an agent) is read again by itself: its controls, step rate and start scene take over, in the **Controls** window and a running game too.
- A script saved while the game plays starts the game over with it, as a fresh Play would, rather than swapping it into the game as it stands, which started each script again (spawning what its `Start` spawns twice) without its own fields, and had the others read scripts that hadn't started yet.
- `entity_update`'s `remove` takes off a component whose script is gone, by the name `scene_tree` shows it with (`ShopSign (missing)`), as **Remove Component** does.

## 0.1.0-alpha.18 - 2026-10-17

- Conveyor belts on a grid: a **Conveyor** lays directed belt cells on a tilemap's cells (rows like `4 2 right`, or `LayPath` from a script) and carries items along them a cell apart at its **Speed**, backing up behind a full end; a belt joining another from the side merges, taking turns, and a **split** cell hands items round its belts. A **Conveyor Port** on a machine takes what reaches a belt's end into its inventory (until it **Holds** enough, leaving the wrong item waiting) and puts what it makes on a belt. Belts are walls to grid movers (`WalkTo` goes round them) and **crossings** let them over, items move in fixed steps of game time so a line plays out the same at 16x, and hundreds of items are drawn a quad each.
- Turned and flipped tiles: a tilemap cell can hold its tile turned a quarter, a half or three quarters, or flipped, so a belt, an arrow or a pipe is drawn once and painted pointing any way, animated tiles too. While painting, Z turns the brush (Shift+Z back) and X flips it (Shift+X upside down); the palette and the cell under the cursor show it turned, Ctrl-click picks up a cell's turn, and undo takes turns back with the tiles. Scripts lay tiles turned with `SetTile(x, y, tile, TileTurn.Turn90)` (and `FillRect`, `FloodFill`), read them with `GetTurn` and change them with `SetTurn`. The turns are the map's own grid, **Turns**: maps that turn nothing save exactly as before. Slope tiles collide turned.
- Graphs in the game: **Entity → UI → Chart** draws a line or bar chart of a few series (a name and a color each, the last **Keep** points of each) for the HUD or the world, with value lines and their numbers, a legend and **X Labels** along the bottom. A script feeds it (`chart.Add("Cards", 120)`, `Set`, `Clear`), or with no code a series names an output of its **Track** entity (`Cards: #4C9AFF = Cards Today`) and the chart takes a point of it every **Sample Every** seconds of game time. Its range fits the points to a round number unless **Max** says. Rewinds and network games keep its points.
- Rooms found from walls: a **Wall Rooms** on a tilemap finds the closed rooms and open areas its walls make (solid tiles, **Wall Tiles** such as doors counted as shut, and colliders on **Wall Layers**), each with an id it keeps as walls change, its cells and area and whether it's open to the outside. It works them out again only when the walls change, raises **Changed** and sends its **Message**, answers which room a cell, point or entity is in and what's in a room, shows `Closed`, `RoomArea:<name>` and `RoomId:<name>` as outputs, and tints each room with **Show**.
- Fixed: while the game runs, what you type in an Inspector text field (a tilemap's rows, a long text) is no longer put back by the live values before you leave the box; a text field is shown again only when its value changes.

## 0.1.0-alpha.17 - 2026-10-17

- Show a label only while the camera is zoomed in, on a backing box fitted to its words: a **Show When**'s **Zoom Below** (for example 7) shows its entity while the main camera's Size is that or less and hides it when the camera pulls out (0 ignores the zoom, it needs no flag, and with a **Needs Flag** both must hold), and a **Text**'s **Background** (a color, transparent by default) and **Padding** (world units) draw a box just behind the letters, as wide as the widest line and as tall as all of them with wrapping, alignment, Vertical Align, a HUD's scale and Order. The UI and text guides and the world-labels demo say so.
- Camera settings for the player, and a glide that hands the camera back. A **Toggle** set to **EdgeScroll** and a **Slider** set to **PanSpeed** (`Game.EdgeScroll`, `Game.PanSpeed`) let a player turn the screen's edges panning off or on and scale the pan speed of every **Camera Controls**: the toggle's choice wins over the camera's own Edge Scroll once made, the speed multiplies Pan Speed (1 changes nothing), and both are remembered and kept through a New Game. `CameraControls.GlideTo(target)` (an entity, a name or a point, with an optional Size and seconds; **Glide To** for a Button or Trigger) glides the view there at the Size it has and leaves the camera the player's on arrival, with no Release to time; a pan key, a drag or the wheel during the glide cancels it. The players and UI guides and the camera focus demo say so.
- A balance simulation can play the whole game over time: tick **Play a whole scene over time** in the Simulations panel and each run plays a **Scene** headless, steps back to back on every core, until its **Time Limit**, the scene changing, `Game.Quit` or **End when** (a flag, or `Director.Cash < 0`). A `SimulationScript` is its strategy: `Setup` before the first step, `Act` every **Act every** seconds of game time (and while a panel pauses the game), `IsOver` and `Done`, playing through the game's own components with `run.Game`. **Flags** set before it starts, **Close panels that pause it** skips an opening letter, **Step rate** plays in coarser steps, and **Track every** takes a point every so many seconds with every point kept (charts thin them for drawing; **Export** writes them all to `... over time.csv`). Seeds, sweeps over a table's cell and a side's table of strategies work as for a matchup.
- Select things as in a strategy game with no code: **Selection Controls** on the camera and a **Selectable** (a **Kind**, **In Box**, **Clickable**, an area) on workers, machines or trucks. A click picks one, a left-drag boxes those inside (not what's off **In Box**), Shift adds or takes out, a double-click picks every one of that kind on screen, Ctrl and a number keeps a control group that the number picks again (twice quickly moves the camera there), and Esc clears the selection, after any window open over the game has closed and before the game menu opens. A right-click with something selected raises `Commanded(target, point, selected)` for the game to carry out; markers (an outline or a nine-sliced **Marker Image**) go round what's selected, and **Busy** leaves the mouse to a tool in hand. Its keys are new built-in actions (**Select**, **Command**, **AddToSelection**, **AssignGroup**, **Group1** to **Group9**) that Game → Controls and Rebind buttons change, and the strategy **Commander** now selects by the same rules and actions, playing as before.

## 0.1.0-alpha.16 - 2026-10-17

- A Table Values line can set a list of names: a property that is an `IReadOnlyList<string>` (a script's `Priorities`, a Game Speed's `Speeds`) takes a text cell split on commas and line breaks, each name trimmed and empty ones dropped, and the Inspector marks it as set from the table like any other field. The data tables guide and its demo say so.

- Fixed: dragging the view of a camera with **Camera Controls** no longer swings back and forth after the mouse moves (on a 60 Hz screen it kept rocking while the button was held, at two steps a frame or fast-forwarded it got worse, and the strategy camera's edge scroll and minimap drag stuttered the same way): the camera goes by where the mouse is on the screen and the camera as it is now, not the world point found through the camera as last drawn, so the view goes where the mouse takes it and stays. Dragged past a **Stay Inside** edge, dragging back moves it at once, and an agent's `game_input` drag moves the view as far as the drag. Scripts get `Input.MouseOnScreen`.

## 0.1.0-alpha.15 - 2026-10-17

- A value saved under a name starting `settings.` (a Toggle's or Slider's **Save As** `settings.difficulty`, a Toggles List's `settings.fire.{row}`) is the player's setting: a **New Game** keeps it, as it keeps volumes and controls.
- A **Toggles List** makes an on/off switch for each row of a data table: put it on an entity with a Layout (an options panel), name its **Table**, and while the game runs it makes a row for each table row with its label (**Label Column**, else the row's name) and a real Toggle kept in the save data as **Save As** says (`fire.{row}`), starting as the **Default Column** says until the player flips it or the game has one saved. **Hide** leaves rows out, and a **Template** entity styles the rows. Scripts read the switches with `Game.SaveData.GetBool("fire.Rain")`.
- Fast-forward that stays exact: a **Game Speed** (Gameplay) runs the game at its **Speed** (speed buttons set it with **Do** `Set Speed`, `Faster` or `Slower`), and while it's **Exact** a faster game takes more steps of the usual length rather than longer ones (`Time.FastForward` from a script), so a sim plays out the same at 16x as at 1x. With a plain high `Time.Scale`, a **Grid Movement** now takes every cell a long step covers, so walkers no longer fall behind in game time.

## 0.1.0-alpha.14 - 2026-10-17

- A **Needs Flag** can name several flags joined by "or": `run.Contracts or run.Work` holds while either is set, on a Show When, panel, door, switch or spawner.
- An **Animation** clip whose frames are written high to low plays backward: `Open 0-3 once, Close 3-0 once` opens a door and closes it with the same frames. The Clip Editor previews it backward too.
- A **Show When** (Gameplay) shows anything only while a flag holds (**Needs Flag** `run.Contracts`, or `not Tutorial`), hidden with what's inside it otherwise and looked at every frame: a HUD that brings in its tools as goals are met, whose hidden buttons can't be clicked or set off by their shortcuts. A flag (or any saved value) named `run.` lasts one run: kept while the game runs and in its saved games, but not written down, so it's unset the next time the game starts. A New Game already forgets every flag.
- A dialogue's **Stop** action (on a button or trigger) and `Game.EndDialogue(dialogue)` end it at once, even mid-line, as saying its last line would: the box hides, the pause is let go, its **Message** is sent and `Finished` is raised, with `CutShort` true. For a Skip button on a cutscene or a tour.
- **Camera Controls** can take the camera over for a while: `FocusOn(entity, size)` glides to a character (or a point) and follows it at a closer Size, inside **Stay Inside**, while the player's pan, drag and wheel wait; `Release()` zooms back out to the Size it had and hands the camera back where it is. **Focus Size** and **Focus Seconds** set the defaults, and a Trigger or a Do button can do **Focus On** and **Release**, for a guided tour or a cutscene with no code.
- Agents' clicks in `game_input` land on a button pinned to the screen even after the camera has moved: put on the HUD, the agent's mouse rests on its spot on the screen as a real one does, so clicking again where it rests clicks a panel that opened there again.
- Fixed: the editor's **Step Frame** (F10) no longer takes F10 from a game that is playing: it steps a paused game only, and while a game plays, a plain Game menu shortcut goes to the game when one of its input actions is bound to that key. Pause (F6), Stop (Shift+F5) and Play Fullscreen (Alt+Enter) still always work.
- Scripts can have Esc before the game menu does: override `ClaimsBack` (`protected override bool ClaimsBack => tool is not null;`) and while it's true Esc and B don't open a **Panel** on its **Open On** (or close one the game goes on behind), so a builder's Esc puts the tool down first and the next Esc opens the menu. Only a running script claims it, never one behind a pausing panel.
- Fixed: a press a panel acts on no longer reaches the game in the same frame. The Esc that closes the game menu doesn't also put a tool down or skip a tour, nor does a panel's **Open On** key, or Enter, A or a shortcut clicking a button that opens or closes a panel; `Input.WasPressed` sees the key the next time it's pressed. Rebinding a key doesn't fire its new action either.
- A **Button** with **Over Panels** keeps taking clicks and its shortcuts while a panel it isn't on is open, even one that pauses the game: a menu bar's Game button opens the game menu over a letter, on top of it, and closing the menu leaves the letter open.
- A **Panel** with **Popup** ticked is a fly-out, dropdown or context menu: a mouse press off it closes it and does nothing else (no button there clicks, scripts don't see the press, and `Game.MouseOnUi` is true for it), and clicking one of its buttons closes it once the button has done what it does, so a pick on a tool palette's fly-out closes it and an **OpenPanel** button on it opens its window in its place.

## 0.1.0-alpha.13 - 2026-10-17

- A **Slider** can be drawn with pictures, as a Bar can: a **Fill Image** shown only as far as it's set (cut off, not squeezed), a **Background Image** for the track that can be a nine-slice (**Background Slice**), and a **Knob Image** at the end of the fill, each tinted by its color. With none set it looks as before.
- Display settings for options menus, with no code: a **Choice** set to **Window Size** offers the 16:9 window sizes that fit the player's screen and resizes the built game's window (which opens at it next time; fullscreen stays borderless at the screen's resolution), one set to **Frame Cap** offers Off, 30, 60, 120, 144, 165 and 240, a **Toggle** set to **VSync** draws frames as fast as the cap allows when off, and a **Slider** set to **UI Scale** makes the HUD and tooltips bigger or smaller. All are remembered, off or as before by default, and scripts have them as `Game.WindowSize`, `Game.VSync`, `Game.FrameCap` and `Game.UiScale`. A built game left fullscreen now opens fullscreen again.
- A **Controls List** makes a game's controls screen by itself: put it on an entity with a Layout (an options panel) and while the game runs it makes a row for every input action, its name and a Rebind button showing all its keys, made again when the actions change. **Actions** picks which and in what order (`DropTool, Pan*`), **Show** lists only the game's own, **Hide** leaves some out (`Debug*`), **Labels** renames them, **Slots** gives each row a button per key, and a **Template** entity styles the rows. The Scene view shows sample rows.
- `{DropTool:all}` in a Text or Tooltip shows every key of an action on the device in use (`Esc / Right Click`), and `{PanUp:1}` its second; `{DropTool}` still shows the first. Scripts have `Input.AllFor("DropTool")` and `Input.SlotFor("PanUp", 1)`.
- Fixed: a Rebind button no longer drops the action's other keys and mouse buttons. Rebinding Drop Tool (Esc and Right Click) to Q keeps Right Click, and Pan Up (W and Up) to I keeps Up: only the first is replaced, or the one in the button's new **Binding Slot** (1 for a secondary key).

## 0.1.0-alpha.12 - 2026-10-17

- Agents change many entities in one call: `entity_update` picks them by a glob on their path (`HUD/Toolbar/Tool *`), by a component they have or by what they're inside, a batch is one undo step that changes nothing if any part fails (listing each entity as ok or with its error), and `"dryRun": true` lists each field's old and new value without changing anything (`entity_create` has it too).

## 0.1.0-alpha.11 - 2026-10-17

- A **Tooltip** fills in input prompts as a Text does: `Key: {OpenWork}` in its Value shows the key that does it now, following the gamepad and the player's rebinding.
- **Help > Check for Updates** opens an **Updates** window straight away that shows it checking, then downloading with a bar, then what came of it: up to date (with the version), why it couldn't check, or the offer to restart. The check after starting still says nothing unless there's an update.
- Answering **Yes** to restarting into a downloaded update really restarts Griff into it: Griff asks about unsaved changes, closes completely (a window left over from dragging a panel no longer keeps it running unseen) and the updater puts the new version in place and starts it.

## 0.1.0-alpha.10 - 2026-10-17

- The **Assets** panel shows the folders as a tree on the left and the picked folder's files as tiles on the right, pictures as thumbnails and everything else with an icon for its kind. Double-clicking, dragging into the scene or onto a folder, and right-clicking work on the tiles as they did on the rows, a slider sizes the tiles, and the **Tree** button brings back the one tree.
- **Expand All** and **Collapse All** buttons on every panel with a tree or sections that fold: the Hierarchy, the Inspector (every component and subheading), the Assets panel (every folder, in either view) and the script editor (every block of code).
- With a tilemap selected, the Scene view's new **Select** tool (V), first under the tile palette, is the cursor: click and drag to select and move things without painting, then pick the Brush (B) to paint again. The tool stays as you left it.
- Panel tabs can be dragged along their group to reorder them, and closed with the **×** on the tab (or a middle-click, or the tab's right-click menu). **View > Panels** brings any panel back, and saved layouts and **Reset Layout** remember the order and which panels are closed.
- A panel can go among the documents: drop its tab on the Scene and Game tabs or in the middle of the scene, and it's a tab beside the scene, prefab and script tabs, filling the whole area (a big **Tables** panel, say). Drag it out again to put it back; layouts remember it.
- A table can open in a tab of its own beside the scene: **Data > Open Table in Tab**, or turn on **Data > Open Tables in Tabs** so opening or double-clicking a table does it. Each tab is the Tables panel's spreadsheet for that table, and the Data menu works on whichever table you used last. The **Tables** panel still works as before.
- The **Sprite Painter**, **Sound Maker**, **Clip Editor**, **Move List** and **Dialogue Editor** open as tabs beside the scene instead of windows: a tab for each picture or sound, a dot on a tab with unsaved changes, and the same "save changes?" question as a scene when you close one.

## 0.1.0-alpha.9 - 2026-10-17

- Camera Controls pans with four new built-in actions, **PanUp**, **PanDown**, **PanLeft** and **PanRight** (WASD, the arrows and the D-pad as before), so **Game > Controls** and players can rebind them, and a game can take W or S for something else.
- A button's **Shortcut Action** clicks it with an input action instead of a fixed key, so a menu key shows in **Game > Controls** and players can rebind it.
- A **Bar** can be drawn with pictures: its **Fill Image** is revealed as it fills, in its **Direction**, cut off rather than squeezed, over a **Background Image** that can be a nine-slice. Its colors tint them.
- Agents: `ui_invoke` on a path ending at a property, or with an argument the tool doesn't take (a `method` beside the path), no longer answers with what the property holds as though something had been called: it says what went wrong. Every MCP tool now refuses arguments it doesn't take, and `ui_invoke` reads lists and objects by name of anything it can read, so `Project.SetControlsAsync` with `[{"PauseTime": ["Space"]}]` sets and saves a project's controls.
- A script spawned during the game no longer stops it with "The behavior isn't running in a game yet" when a **Tooltip**, **Text**, **Counter**, **Bar** or wire shows one of its outputs that uses `Game`: a script's outputs are read once it has started, from the next step. A **Tooltip** now reads its output only while it's the one hovered.
- A **Grid Movement** can walk somewhere by itself: give it a **Destination** (cells or an entity), use its **Walk To** action, or call `WalkTo` from a script, and it goes to the nearest free cell round walls, solid things and other movers, waits when someone's in the way, and steps aside in a narrow aisle so two walkers get past each other. **Arrived** and **Blocked** say how it went, and a tilemap's `FindSteps` plans such a way for scripts.

## 0.1.0-alpha.8 - 2026-10-17

- **Camera Controls**: put it on the camera and the player pans with the arrow keys or WASD, drags the world with the middle or right mouse button, zooms toward the mouse with the wheel between a smallest and largest size, and can pan at the screen's edge. **Stay Inside** keeps the view inside a level, even as the level grows. No code needed.
- A camera's **Hud Size** keeps the HUD the same size and in the same place on the screen while the camera zooms, and its buttons still click where they're drawn.
- The Scene view shows anything pinned with a **Screen Anchor** where the game will show it, in its corner of the camera's frame, and dragging it there sets its **Offset**.
- **Sort Group**: a thing made of several sprites (a stack of crates on a pallet, a machine and its operator) sorts by Y as one, so characters walk in front of all of it or behind all of it.
- Tilemaps with no image can give each tile number its own **Placeholder Colors**, with an optional **Placeholder Grid**, to block out floors and walls before there's any art.
- Text can hang from the top or stand on the bottom (**Vertical Align**) and wrap between words to a **Wrap Width**.
- **Layout** lines up an entity's children in a list, a row or a grid: cards and rows spawned into it take their places, and removing one closes the gap. It can clip what doesn't fit and scroll with the wheel.
- **Tooltip** shows a box by the mouse after a short delay, or writes into a Text you name, while the mouse rests on an entity. A script can set what it says, or it can show an output.
- **Game > New Random Numbers Each Game** starts each Play with different random numbers, instead of the same ones every Play until the editor restarts.
- **Game > Play Fullscreen** (Alt+Enter) plays the game over the whole screen, and switches back to the Game tab without stopping the game. F6, F10 and Shift+F5 now also work while a game running in the Game tab has the keyboard.
- For agents (MCP): clicking a button by its label finds a button whose label is on the button itself, **Scene_Open** takes a scene's name, and **Edit_Undo** and **Edit_Redo** can name the scene or prefab to undo in.

## 0.1.0-alpha.7 - 2026-10-16

- Release plumbing only: the release run no longer runs the tests itself, because the build servers' software OpenGL kept failing them for no real reason. No change to the editor.

## 0.1.0-alpha.6 - 2026-10-16

- Release plumbing only: a release now waits for the tagged commit's automatic checks and stops unless they passed. No change to the editor.

## 0.1.0-alpha.5 - 2026-10-16

- Release plumbing only: the tests that draw offscreen no longer trip over each other on the build servers. No change to the editor.

## 0.1.0-alpha.4 - 2026-10-16

- Release plumbing only: the release's tests run the way the automatic checks do. No change to the editor.

## 0.1.0-alpha.3 - 2026-10-16

- File > Close Project puts the editor back to having no project open.
- The Add lists describe a component only when you ask, with an info mark on each row instead of a tooltip over the names.
- Each genre's editor parts (the Fighting Move List, the Rhythm chart editor and the like) now live with their genre.

## 0.1.0-alpha.2 - 2026-10-16

- One undo for everything: scenes, prefabs, tables, simulations, rhythm charts, the Sprite Painter and clips. Edit > Undo and Redo, Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z go to whichever you changed or worked in last.
- The Scene view draws with the game's own renderer, so what you see while editing matches what plays.
- Mistakes in a game's data are reported in one place, in Output and flagged in the Inspector, with the entity and component they were found on.
- Entities are found by name the same way everywhere: scripts, triggers, counters, switches and the Inspector.
- Renaming an action, output or component keeps the old name working, and names that point at nothing are flagged.
- Rewinding a game restores all of its state: scripts' private fields, timers, tweens, physics and random numbers too.
- Prefabs are read once and copied when spawned, so spawning is faster, in exported games too.
- Counters can count any output, not just the built-in goals, moves and score.
- Settings such as the number of worker threads belong to each game, so several games (the web player, multiplayer, tests) no longer share them.
- The Entity menu's presets are available to templates and agents, not only the menu.
- Networking, sound, input and files sit behind services that scripts and modules can ask for.
- Much of the engine was reorganized into separate parts for gameplay, physics and rendering; games keep working as before.

## 0.1.0-alpha.1 - 2026-10-15

- The first release: the Griff editor for 64-bit Windows, with scenes, entities and components, a scripting setup in C#, the Sprite Painter and Sound Maker, tables and balance simulations, multiplayer, and exporting a game to Windows or the web.
- Installer that keeps Griff up to date, and a portable zip.
