# Demos

Each `.demo` file is a short, narrated walkthrough of one feature or change. Griff plays it by driving its own
editor with real mouse and keyboard input, speaking the narration aloud, spotlighting what it talks about, and
showing a control bar to pause, change speed, skip or rewind between chapters, or exit.

```powershell
tools\demo.ps1 demos\single-file-builds.demo           # play it, narrated (move the mouse to pause)
tools\demo.ps1 demos\single-file-builds.demo -Check    # run it silently at speed; PASS or FAIL
tools\demo.ps1 -All -Check                             # check every demo (only when asked for)
tools\demo.ps1 -Tour                                   # the full guided tour that builds Ember Hollow
```

Demos are kept current. A change that alters something a demo shows updates that demo in the same commit. Only the
demos for the change are checked and played; other demos (`-All -Check`, the tour) run only when asked for.

## Writing a demo

One command per line, `command: argument`. Blank lines and `#` comments are ignored. Lines before the first
`chapter:` set things up behind a title card; each `chapter:` starts a part the viewer can skip or rewind to.

```
title: Single-file builds
intro: Build Game now makes one exe.
project: Platformer | Jump Kit

chapter: One exe, nothing to install | Game > Build Game
say: Build Game now makes a single exe.
build: standalone
```

| Command | What it does |
| --- | --- |
| `title: <text>` / `intro: <text>` | The opening card's title and subtitle. |
| `project: <Template> [\| <Name>]` | A fresh project from a template: `Empty`, `Platformer`, `TopDown`, `Arcade`, `Strategy`, `Shooter`, `Sandbox`, `Contraption` or `Multiplayer`. |
| `open: <folder>` | A copy of an existing project (the original is never changed). |
| `scene: <name>` | Open a scene by name. |
| `chapter: <title> [\| <subtitle>]` | Start a chapter: a card, a dot on the control bar, a rewind point. |
| `say: <text>` | Narrate (spoken and captioned). |
| `menu: <Menu> > <Item>` | Click through the main menu, e.g. `menu: Game > Build Game`. |
| `create: <thing>` | Shorthand for `menu: Entity > Create <thing>`, e.g. `create: Platformer Player`. |
| `key: <shortcut>` | Press a shortcut, e.g. `key: Ctrl+Shift+B`, `key: F5`. |
| `select: <entity>` / `deselect` | Pick an entity in the Hierarchy, or nothing. `select: Ctrl+Sign` Ctrl-clicks its row (adding it to the selection, or taking it out), `select: Shift+Coins` Shift-clicks it (selecting every row from the last one clicked). |
| `expectselected: <entity>, <entity>...` | Fail unless exactly these are selected, in the order they were picked. |
| `expectcount: <entity> = <n>` | Fail unless the open scene has that many entities with that name. |
| `sceneclick: <point>` | Click in the Scene view at an entity (by name, `Slime`) or a world point (`3, 2`). `sceneclick: Ctrl+Slime` (or `Shift+`) adds it to the selection, or takes it out. |
| `scenedrag: <point> > <point>` | Drag in the Scene view between two points, as `sceneclick` takes them: from an entity to move the selection, from empty space to draw a selection box. `scenedrag: Shift+-2, 3 > 4, -1` holds Shift, adding the box to the selection. |
| `expectat: <entity> = <x>, <y>` | Fail unless the entity is at that world position (within a tenth of a unit). |
| `expectmixed: <Component>.<Field>[.<Axis>]` | Fail unless the Inspector shows that field (or one axis of it, `Transform.Position.Y`) as mixed: the selected entities' values differ. |
| `expectvalue: <entity>.<Component>.<Field> = <value>` | Fail unless the (first) entity with that name has that value, e.g. `expectvalue: Red Gem.Sprite.Tint = #FFEC27`. Colors compare as colors and numbers as numbers. |
| `rename: <name>` | Rename the selected entity. |
| `add: <Component>` | Add Component, e.g. `add: Health`. |
| `catalog` | Open the component catalog with the Inspector's Catalog button. |
| `browse: <Component>` | Open the catalog (if needed), search for a component and pick it, e.g. `browse: Patrol`. `click: catalog:add` adds it. |
| `set: <Component>.<Field> = <value>` | Type into the Inspector. Any field: numbers, text, colors, checkboxes (`true`/`false`), choices, tile rows (`\n` between rows). Vectors take `x, y` or one axis: `set: Transform.Position.Y = 2`. Field names as shown (`Jump Height`) or as in code (`JumpHeight`). |
| `spot: <target> [\| <label>]` / `unspot` | Spotlight something until the next spot or `unspot`. |
| `explain: <target> \| <label> \| <text>` | Spotlight something while saying `<text>`. |
| `click: <target>` | Click something. |
| `look: <x>, <y>, <half height>` | Glide the Scene view to show a part of the world. |
| `play` / `stop` / `pause` | Play mode (F5, Shift+F5, F6). |
| `players: <n>` | Game > Play with n Players: the Game tab hosts, and each other player gets a window that joins it. Waits until everyone's in. |
| `player: <n>` | Play as player n from now on: `tap`, `hold`, `gameclick` and the other game commands go to their game and window (1 is the Game tab). `stop` goes back to player 1. |
| `tap: <Key>` / `hold: <Key> <seconds>` | Press a key in the running game, e.g. `hold: Right 1.5`, `tap: Space`. `hold: LeftShift+Right 1` holds both. |
| `type: <text>` | Type into whatever has focus (`\n` presses Enter). Quotes keep spaces at the ends: `type: " * 2"`. |
| `wait: <seconds>` | Pause the demo. |
| `code: <file> [\| <title>]` / `hidecode` | Type a file (relative to the project) out in the code card. |
| `save` | Ctrl+S: saves the script in a script tab (and recompiles), the scene anywhere else. |
| `edit: <Component>` | Click a script component's Edit Script button in the Inspector, opening it in the script editor. |
| `caret: <code> [\| before]` | Click in the script to put the caret just after (or before) the first match of `<code>`. |
| `hover: <code>` | Rest the mouse on `<code>` until its hover info shows. |
| `codekey: <shortcut>` | Press a shortcut in the script editor, e.g. `codekey: Ctrl+F`, `codekey: Tab`. |
| `tune: <Property> = <value>` | Type a value into a property in the script editor's component panel. |
| `expect: <code>` | Fail unless the script contains `<code>`: checks that what was typed came out as narrated. |
| `build: standalone` / `build: small` | Game > Build Game (or Build Small Game) and wait for the exe. |
| `maker` | Wait for the Sound Maker to open (after `menu: Audio > New Sound...` or a field's Sound Maker button). |
| `slide: <Setting> = <value>` | Drag a Sound Maker slider to a value, e.g. `slide: Slide = 0.4`, `slide: Tempo = 110`. |
| `name: <text>` | Type the Sound Maker's file name (Save puts it in `Sounds/` or `Music/`). |
| `pick: <Component>.<Field> = <sound>` | Choose a sound from a sound field's list of the project's sounds, e.g. `pick: Health.HurtSound = Sounds/hit.wav`. |
| `expectfile: <path>` | Fail unless the file (relative to Assets) exists: checks a Save really saved. |
| `painter` | Wait for the Sprite Painter to open (after `menu: Art > New Sprite...` or an image field's Paint button). |
| `paint: <x>,<y> [> <x>,<y> ...] [\| right]` | Paint on the Sprite Painter's canvas with the real mouse, through those pixels of the frame (from 0, 0 at its top left), with the selected tool: one pixel is a click. `\| right` drags with the right button, which erases. |
| `artname: <text>` | Type the Sprite Painter's file name (Save puts it in `Sprites/` or `Tiles/`). |
| `pickimage: <Component>.<Field> = <image>` | Choose an image from an image field's list of the project's pictures, e.g. `pickimage: Tilemap.Image = Tiles/cave.png`. |
| `expectart: <path> [\| <frames>]` | Fail unless the picture (relative to Assets) was saved by the Sprite Painter, with that many frames or tiles. |
| `controls` | Wait for the Controls window to open (after `menu: Game > Controls...`). |
| `action: <name>` | Pick an action in the Controls window, e.g. `action: Jump`. |
| `bind: <key>` | Click Press a Key and press it, e.g. `bind: W`, `bind: LeftShift`, `bind: Mouse Right`. |
| `bindpad: <button>` | Add a gamepad button from Add Button's list, e.g. `bindpad: LB`, `bindpad: A`. |
| `actionname: <text>` | Type a new name for the selected action (the game's own actions only). |
| `expectcontrol: <action> = <binding>` | Fail unless project.griff gives the action that binding, e.g. `expectcontrol: Jump = W`. |
| `gameclick: <x>, <y>` | Click a point in the running game's world (where its camera shows it now) with the real mouse. Fails if it's off screen or under a HUD. Any game point can also be an entity's middle, `Crate`, or a point from it, `Player + 1.5, -1` (the camera follows the player, so this keeps pointing at the same spot). In a network game, a player's name, `Player 1`, is their character. |
| `mouselook: <x>[, <y>] <seconds>` | Move the real mouse by that many pixels (right and up are positive) over that long, e.g. `mouselook: 300 1`: turns a first-person game. Clicks the game first if it isn't holding the mouse yet. |
| `gameorder: <x>, <y>` | Right-click a point in the running game's world: a strategy game's orders, putting a block down. |
| `gamehover: <point>` | Move the real mouse to a point in the running game, e.g. before `tap: E` presses a wire button under it. |
| `gamehold: <point> \| <seconds> [\| right]` | Hold the left (or right) mouse button on a point in the running game: digging a block, e.g. `gamehold: Player + 0, -1.4 \| 0.8`. |
| `gamebutton: [<kind>:]<label>` | Click a button on the sandbox HUD by its label: a hotbar slot, recipe, tool, prop or port, e.g. `gamebutton: tool:Spawn`, `gamebutton: recipe:Planks`. |
| `gamedrag: <x1>, <y1>, <x2>, <y2>` | Drag from one world point to another in the running game: a strategy game's selection box, or carrying something with the grab tool (`gamedrag: Crate > Plate + 0, 1.5`). |
| `minimap: <x>, <y>` | Click a strategy game's minimap where it shows a world point: moves the camera there, or sends units after `tap: A`. |
| `minimaporder: <x>, <y>` | Right-click the minimap where it shows a world point: sends the selected units there, or sets a building's rally point. |
| `ready: <building> \| <seconds>` | Wait (real time) until one of the player's buildings with that name is finished, e.g. `ready: Barracks \| 40`. |
| `afford: <cost> \| <seconds>` | Wait (real time) until the player's team can pay for something, e.g. `afford: 180 Gold \| 60`. |
| `have: <count> <name> \| <seconds>` | Wait (real time) until the player has at least that many units (or finished buildings) with that name, e.g. `have: 12 Soldier \| 120`. |
| `waitgone: <entity> \| <seconds>` | Wait (real time) until no entity with that name shows in the running game, e.g. a wall a sensor opens. |
| `waitfor: <entity> \| <seconds>` | Wait (real time) until an entity with that name shows in the running game, e.g. `waitfor: Victory \| 240`. |

The waits that watch the running game fail when their time runs out, logging how each team stands every ten seconds
while they wait (`%TEMP%\griff-demos\demo.log`).

Targets: `panel:Inspector` (or `Hierarchy`, `Assets`, `Output`), `logo` (the griffin in the title bar), `scene`, `game`, `player:2` (a player's window, or the Game tab for 1), `toolbar:Game.Play`
(a command id), `field:Health.Max`, `component:Health`, `entity:Player`, `lock:Backdrop` (the lock button on a Hierarchy row), `menu:Game`, and
`world:left,top,right,bottom` for a box in the Scene view, `gameworld:left,top,right,bottom` for a box in the running
game's world, and `hud:minimap`, `hud:card`, `hud:selection` or `hud:resources` for the parts of a strategy game's HUD, and `hud:hotbar`, `hud:crafting`, `hud:tools`, `hud:props` or `hud:ports` for a sandbox game's.
In the component catalog: `catalog:button` (the
Inspector's Catalog button), `catalog` (the window), `catalog:search`, `catalog:list`, `catalog:docs`,
`catalog:requires`, `catalog:workswith`, `catalog:neededby`, `catalog:properties`, `catalog:plan`, `catalog:add`,
`catalog:close`, `catalog:item:Body` (a row in the list), `catalog:link:Body` (a link in the details) and
`catalog:property:Jump Height`.

In the Sound Maker: `maker` (the window), `maker:preset:Coin`, `maker:wave:Square`, `maker:mood:Spooky`,
`maker:mode:Effect` / `maker:mode:Music`, `maker:slider:Slide` (a setting's row), `maker:mutate`, `maker:newtune`,
`maker:play`, `maker:stop`, `maker:save`, `maker:close`, `maker:name`, `maker:waveform`, `maker:presets`,
`maker:waves`, `maker:settings`, `maker:moods`, `maker:music`, `maker:parts` and `maker:tempo`. A sound field's
buttons in the Inspector: `sound:Health.HurtSound:play`, `sound:...:choose` and `sound:...:make`.

In the Sprite Painter: `painter` (the window), `painter:tool:Fill`, `painter:color:8` (a palette swatch, from 0, or
`painter:color:#FF004D`), `painter:idea:Creature`, `painter:frame:2` (a frame or tile by its label), `painter:mode:Sprite` /
`painter:mode:Tileset`, `painter:another`, `painter:canvas`, `painter:preview`, `painter:frames`, `painter:addframe`,
`painter:removeframe`, `painter:mirror`, `painter:onion`, `painter:outline`, `painter:save`, `painter:close`,
`painter:name`, `painter:palette`, `painter:swatches`, `painter:hex`, `painter:current`, `painter:fps`, `painter:tools`,
`painter:ideas`, `painter:options`, `painter:layout` and `painter:size`. An image field's buttons in the Inspector:
`image:Sprite.Image:choose` and `image:Sprite.Image:paint`.

In the Controls window: `controls` (the window), `controls:actions` (the list), `controls:action:Jump` (a row),
`controls:addaction`, `controls:move`, `controls:name`, `controls:builtin`, `controls:usedby`, `controls:keys`,
`controls:pads`, `controls:chip:W` (a key or button on the selected action), `controls:remove:W` (its ×),
`controls:listenkey`, `controls:addpad`, `controls:listenpad`, `controls:playerchoice`, `controls:code`,
`controls:reset`, `controls:delete`, `controls:status` and `controls:close`.

In the script editor (the script tab showing): `edit:Ship` (a component's Edit Script button in the Inspector),
`tab:Ship.cs` (a document tab, by its title), `code:editor`, `code:panel` (the component panel), `code:property:Speed` (a
property's row in it), `code:name:Speed` (its name, which jumps to the code), `code:instances`, `code:notice`,
`code:problems` (the error counts), `code:list` (the problems or references list), `code:find`, `code:status`,
`code:breadcrumb`, `code:toggle` (the Component button), `code:format`, `code:tab`, `code:text:Speed * Time` (some
code), `code:line:12`, and while they show, `code:completion`, `code:signature` and `code:quickinfo`.

Keep a demo to a few minutes: show the change working, the way someone would use it, and say why it matters.
