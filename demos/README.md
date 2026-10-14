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
| `project: <Template> [\| <Name>]` | A fresh project from any template New Project lists, by name or title, ignoring case, spaces and dashes: `Empty`, `Platformer`, `TopDown`, `Arcade`, `Match3` (or `Match-3`), `FallingBlocks` and so on. |
| `open: <folder>` | A copy of an existing project (the original is never changed). |
| `scene: <name>` | Open a scene by name. |
| `chapter: <title> [\| <subtitle>]` | Start a chapter: a card, a dot on the control bar, a rewind point. |
| `say: <text>` | Narrate (spoken and captioned). |
| `menu: <Menu> > <Item>` | Click through the main menu, e.g. `menu: Game > Build Game`, and into submenus: `menu: View > Layouts > Level Building`. |
| `create: <thing>` | Shorthand for `menu: Entity > Create <thing>`, e.g. `create: Platformer Player`. |
| `key: <shortcut>` | Press a shortcut, e.g. `key: Ctrl+Shift+B`, `key: F5`. |
| `select: <entity>` / `deselect` | Pick an entity in the Hierarchy, or nothing. `select: Ctrl+Sign` Ctrl-clicks its row (adding it to the selection, or taking it out), `select: Shift+Coins` Shift-clicks it (selecting every row from the last one clicked). |
| `expectselected: <entity>, <entity>...` | Fail unless exactly these are selected, in the order they were picked. |
| `expectcount: <entity> = <n>` | Fail unless the open scene has that many entities with that name. |
| `sceneclick: <point>` | Click in the Scene view at an entity (by name, `Slime`) or a world point (`3, 2`). `sceneclick: Ctrl+Slime` (or `Shift+`) adds it to the selection, or takes it out. |
| `scenedrag: <point> > <point>` | Drag in the Scene view between two points, as `sceneclick` takes them: from an entity to move the selection, from empty space to draw a selection box. `scenedrag: Shift+-2, 3 > 4, -1` holds Shift, adding the box to the selection. |
| `scenehover: <point>` | Rest the mouse on a point in the Scene view, as `sceneclick` takes them, e.g. to show the area a tilemap's Fill would cover there. |
| `rotate: <degrees>` | Drag the Scene view's rotate handle that many degrees counterclockwise (negative for clockwise) around the selection's pivot. `rotate: Ctrl+45` holds Ctrl, snapping to 15 degrees. |
| `scale: <handle> = <factor>[, <factor>]` | Drag one of the Scene view's scale handles (`top-left`, `top`, `top-right`, `right`, `bottom-right`, `bottom`, `bottom-left`, `left`) to where it scales the selection by that much along its own axes: `scale: right = 3` triples the width, `scale: top-right = 2, 1.5` sets both. `scale: Ctrl+Shift+top-right = 2` holds Ctrl (snapping to tenths) and Shift (keeping proportions). |
| `resize: [<Component>.]<handle> = <size>[, <size>]` | With Resize or Collider handles out (`menu: View > Resize Handles`), drag one of the primary selection's resize handles, named as `scale:` names them, to where its sprite or collider gets that size in its own units: `resize: right = 4`, `resize: BoxCollider.top-right = 2, 1`, or a circle's radius, `resize: CircleCollider.right = 0.75`. One number on a corner keeps the proportions. `resize: Ctrl+right = 4` holds Ctrl (snapping to half units). |
| `distance: <Component>.<Property> = <value>` | With Distance handles out (`menu: View > Distance Handles`), drag the knob of one of the primary selection's distances straight out or in to where it's that many world units: `distance: Light.Radius = 6`. `distance: Ctrl+Chase.Range = 4` holds Ctrl (snapping to half units). |
| `path: <knob> = <where>` | Drag a knob on the primary selection's path: a mover's point to that offset from its start (`path: Mover.2 = 4, 3`), the plus that adds a point (`path: Mover.+3 = 6, 3` adds point 3 there), a grid patrol's point (or plus) to the middle of the cell that many cells from where it was placed (`path: GridPatrol.1 = 5, -2`, `path: GridPatrol.+2 = 5, 0`), or a patrol's turn to that far out on its side (`path: Patrol.Left = 3`; 0 clears it). `path: Ctrl+Mover.1 = 0, 4` holds Ctrl (snapping to half units). |
| `removepoint: <knob>` | Double-click a knob on the primary selection's path, taking the point off: `removepoint: Mover.2`, or `removepoint: Patrol.Right` to clear a patrol's turn. |
| `expectat: <entity> = <x>, <y>` | Fail unless the entity is at that world position (within a tenth of a unit). |
| `expecttile: <entity> <column>, <row> = <tile>` | Fail unless the tilemap's cell (counted from its top left) holds that tile, or `-1` for empty, e.g. `expecttile: Walls 27, 3 = 1`. |
| `expectlink: <from> > <to> = <fields>` | Fail unless the Scene view draws an arrow from one entity to the other labelled with exactly those fields (`expectlink: Lever > Bridge = Switch Targets`), or `none` for no arrow. |
| `expectmixed: <Component>.<Field>[.<Axis>]` | Fail unless the Inspector shows that field (or one axis of it, `Transform.Position.Y`) as mixed: the selected entities' values differ. |
| `expectflagged: <Component>.<Field> = <names>` | Fail unless the Inspector flags that field as naming something that isn't there: exactly those names (comma-separated), or `none`. |
| `expectvalue: <entity>.<Component>.<Field>[.<Axis>] = <value>` | Fail unless the (first) entity with that name has that value, e.g. `expectvalue: Red Gem.Sprite.Tint = #FFEC27`, or one axis of a vector, `expectvalue: Plank.Transform.Rotation.Z = 45`. Colors compare as colors and numbers as numbers; tile rows are written as `set:` takes them, with `\n` between rows, and text of several lines the same way (`expectvalue: Dialogue Box.Text.Value = Want a sword?\n> Yes <\nNo`). |
| `expectprefab: <entity> = <prefab>` | Fail unless the entity is a placed copy of that prefab (`expectprefab: Gem 2 = Gem`), or `none` for not a copy. |
| `expectoverrides: <entity> = <changes>` | Fail unless a prefab copy changes exactly these from its prefab, as agents see them (`expectoverrides: Gem 3 = Sprite.Tint, Pickup.Amount`, `added Light`, `Shine: Light.Radius` for something inside it), or `none`. |
| `revertfield: <Component>.<Field>` / `applyfield: <Component>.<Field>` | Click the mark by a field the selected copy changed from its prefab, then Revert to Prefab (or Apply to Prefab), e.g. `revertfield: Pickup.Amount`. |
| `answer: <button>` | Click a button on the message box the editor is showing, e.g. `answer: Yes` after `menu: Entity > Save as Prefab` asks before saving over a prefab. |
| `prompt: <text>` | Type into the prompt the editor is showing (a name to rename to, a new folder's) and press Enter. |
| `assetmenu: <path> > <item>` | Right-click an asset's row in the Assets panel (its path in Assets, opening the folders it's in) and pick an item from its menu, e.g. `assetmenu: Prefabs/Gem.prefab > Rename...`, then `prompt: Blue Gem`. |
| `assetdrop: <path> > <folder>` | Drag an asset's row onto a folder's row in the Assets panel with the real mouse, e.g. `assetdrop: Prefabs/Gem.prefab > Pickups`. |
| `expectlinked: <entity> = <path>` | Fail unless the entity is a copy of the prefab at that path in Assets, e.g. `expectlinked: Gem 2 = Pickups/Gem.prefab`. |
| `dock: <panel> > <where>` | Drag a panel's tab with the real mouse to dock it: on a side of the window (`dock: Inspector > left`), on a side of another panel or of the documents (`dock: Assets > bottom of Hierarchy`, `dock: Output > right of Documents`), or onto a panel to tab it with that one (`dock: Output > Inspector`, floating or not). |
| `float: <panel> [> <x>, <y>]` | Drag a panel's tab out over the middle of the document area (moved that far, in screen units), floating it in a window of its own. |
| `closefloat: <panel>` | Click the close button on the floating window a panel is in, putting its panels back. |
| `expectlayout: <summary>` | Fail unless the panels are laid out as `PanelLayout.Summary` says, e.g. `Hierarchy \| (Documents / Assets, Output) \| Inspector`: `\|` between parts side by side, `/` between parts one above another, `,` between tabs, then `; floating: Output` for each floating window. |
| `expectprofiler: <what>` | Fail unless the Profiler panel shows it: `frames` (it has recorded some), `recording = true` or `false`, `entities = <n>` (as it counted them, within five seconds), or `cost = <name>` (it shows what a kind of script, a step or a part of building the picture costs, such as `Sprite` or `Sorting`), or `no garbage = <name>` (it shows that cost, making no garbage), e.g. `expectprofiler: entities = 2006`, `expectprofiler: cost = Bunny` or `expectprofiler: no garbage = Lights`. |
| `rename: <name>` | Rename the selected entity. |
| `add: <Component>` | Add Component: opens the list, types the name and presses Enter, e.g. `add: Health`. It brings what the component needs, as the list does (`add: Patrol` adds a BoxCollider and a Body first). |
| `addlist` | Open the Add Component list with nothing typed, to show it: `type: patr` filters it, `key: Enter` adds the one picked, `click: addlist:item:Patrol` adds that one. |
| `expectcomponents: <entity> = <components>` | Fail unless the (first) entity with that name has exactly these components, in order, by type name: `expectcomponents: Slime = Transform, Sprite, BoxCollider, Body, Patrol`. |
| `catalog` | Open the component catalog with the Inspector's Catalog button. |
| `browse: <Component>` | Open the catalog (if needed), search for a component and pick it, e.g. `browse: Patrol`. `click: catalog:add` adds it. |
| `componentmenu: <Component> > <Item>` | Right-click a component's header in the Inspector and pick from its menu: `componentmenu: Health > Move Up` (or `Reset to Defaults`, `Copy Values`, `Paste Health Values`, `Move Down`, `Add to All`, `Remove Component`). Fails if the item is greyed out. |
| `expectpartial: <Component> = <note>` | Fail unless the Inspector shows that component as only some of the selection have it, with that note (`expectpartial: Light = on 1 of 3`), or `none` for all of them having it. |
| `set: <Component>.<Field> = <value>` | Type into the Inspector. Any field: numbers, text, colors, checkboxes (`true`/`false`), choices, tile rows (`\n` between rows). Vectors take `x, y` or one axis: `set: Transform.Position.Y = 2`. Field names as shown (`Jump Height`) or as in code (`JumpHeight`). |
| `scrub: <Component>.<Field> = <value>` | Drag a number field by its name with the real mouse until it reads that value, e.g. `scrub: PlatformerMovement.Acceleration = 90`. |
| `nudge: <Component>.<Field> = up\|down [<times>]` | Click into a number field and press Up (or Down) that many times, e.g. `nudge: PlatformerMovement.Speed = up 3`. |
| `tip: <target>` | Rest the mouse on something until its tooltip shows, e.g. `tip: label:PlatformerMovement.Acceleration` (a field's name) or `tip: heading:Mover` (a component's). |
| `expectfolded: <Component>[.<Group>] = true\|false` | Fail unless the component's section in the Inspector is folded shut (`true`) or open, or with a group, that subheading in it (`expectfolded: PlatformerMovement.Dash = true`). Fold it with `click: fold:<Component>` or `click: fold:<Component>.<Group>`. |
| `expectfaded: <Component>.<Field> = true\|false` | Fail unless the field is faded in the Inspector (`true`): unused while what it depends on is off, as **Dash Speed** is with **Dash** off. |
| `expectaxes: <Component>.<Field> = <axes>` | Fail unless the Inspector shows those axes of a vector: a Transform in 2D shows `Transform.Position = X, Y` and `Transform.Rotation = Z` (its one angle), and after `click: transform:3d`, `X, Y, Z`. |
| `expectmatches: fields\|entities = <n>` | Fail unless the Inspector's filter (`fields`) or the Hierarchy's (`entities`) lets that many through: `click: filter:fields`, `type: speed`, then `expectmatches: fields = 6`. `key: Escape` empties a filter. |
| `colordrag: <strip> = <value>` | Drag in the open color picker with the real mouse (open it with `click: color:Sprite.Tint`): `colordrag: hue = 200` (degrees), `colordrag: alpha = 0.5`, or `colordrag: square = 0.8, 0.6` (how strong, how bright). Clicking the swatch again shuts it. |
| `spot: <target> [\| <label>]` / `unspot` | Spotlight something until the next spot or `unspot`. |
| `explain: <target> \| <label> \| <text>` | Spotlight something while saying `<text>`. |
| `click: <target>` | Click something. |
| `look: <x>, <y>, <half height>` | Glide the Scene view to show a part of the world. |
| `play` / `stop` / `pause` | Play mode (F5, Shift+F5, F6). |
| `rewind: <fraction>` | With the game paused, drag the Rewind bar's knob with the real mouse that far along it (0 is the oldest snapshot, 1 now), e.g. `rewind: 0.3`: the game goes back to then. Fails if it doesn't rewind. `pause` again resumes from there. |
| `players: <n>` | Game > Play with n Players: the Game tab hosts, and each other player gets a window that joins it. Waits until everyone's in. |
| `player: <n>` | Play as player n from now on: `tap`, `hold`, `gameclick` and the other game commands go to their game and window (1 is the Game tab). `stop` goes back to player 1. |
| `tap: <Key>` / `hold: <Key> <seconds>` | Press a key in the running game, e.g. `hold: Right 1.5`, `tap: Space`. `hold: LeftShift+Right 1` holds both. |
| `type: <text>` | Type into whatever has focus (`\n` presses Enter). Quotes keep spaces at the ends: `type: " * 2"`. |
| `wait: <seconds>` | Pause the demo. |
| `write: <path> \| <file>` | Write a file kept beside the demo (`<file>`, relative to the `.demo`: a script, or a picture such as a tileset) into the project's Assets at `<path>`, as an agent's `asset_write` does: a script compiles straight away, and the demo fails if it doesn't. E.g. `write: Scripts/Wind.cs \| game-systems/Wind.cs`, then `code: Assets/Scripts/Wind.cs` to show it. |
| `code: <file> [\| <title>]` / `hidecode` | Type a file (relative to the project) out in the code card. |
| `save` | Ctrl+S: saves the script in a script tab (and recompiles), the scene anywhere else. |
| `edit: <Component>` | Click a script component's Edit Script button in the Inspector, opening it in the script editor. |
| `caret: <code> [\| before]` | Click in the script to put the caret just after (or before) the first match of `<code>`. |
| `hover: <code>` | Rest the mouse on `<code>` until its hover info shows. |
| `codekey: <shortcut>` | Press a shortcut in the script editor, e.g. `codekey: Ctrl+F`, `codekey: Tab`. |
| `expectstopped: <code>` | Wait (up to 8 seconds) for the game to stop at a breakpoint (or the end of a step) on the line with that code, e.g. `expectstopped: _cooldown = 1 / FireRate` after `click: code:breakpoint:_cooldown = 1 / FireRate` and firing, or after `click: code:stepover`; `expectstopped: none` for it not to be stopped at one (after `pause` resumes it). |
| `tune: <Property> = <value>` | Type a value into a property in the script editor's component panel. |
| `expect: <code>` | Fail unless the script contains `<code>`: checks that what was typed came out as narrated. |
| `build: standalone` / `build: small` | Game > Build Game (or Build Small Game) and wait for the exe. |
| `maker` | Wait for the Sound Maker to open (after `menu: Audio > New Sound...` or a field's Sound Maker button). |
| `slide: <Setting> = <value>` | Drag a Sound Maker slider to a value, e.g. `slide: Slide = 0.4`, `slide: Tempo = 110`. |
| `name: <text>` | Type the Sound Maker's file name (Save puts it in `Sounds/` or `Music/`). |
| `pick: <Component>.<Field> = <name>` | Choose from a field's list, typing the start of the name to narrow it first: a sound (`pick: Health.HurtSound = Sounds/hit.wav`), an image, an entity (`pick: Switch.Targets = Lift`), a prefab (`pick: Spawner.Prefab = Enemy`), a scene, an input action, a layer or a message. On a field that names several, it adds it, or takes it out when it's already there. `pickentity:` is the same. |
| `dropentity: <entity> > <Component>.<Field>` | Drag an entity's row from the Hierarchy onto a field that names entities, with the real mouse, e.g. `dropentity: Slime > Follow.Target`. |
| `expectfile: <path>` | Fail unless the file (relative to Assets) exists: checks a Save really saved. |
| `painter` | Wait for the Sprite Painter to open (after `menu: Art > New Sprite...` or an image field's Paint button). |
| `paint: <x>,<y> [> <x>,<y> ...] [\| right]` | Paint on the Sprite Painter's canvas with the real mouse, through those pixels of the frame (from 0, 0 at its top left), with the selected tool: one pixel is a click. `\| right` drags with the right button, which erases. |
| `artname: <text>` | Type the Sprite Painter's file name (Save puts it in `Sprites/` or `Tiles/`). |
| `pickimage: <Component>.<Field> = <image>` | The same as `pick:`, for an image, e.g. `pickimage: Tilemap.Image = Tiles/cave.png`. |
| `clips` | Wait for the Clip Editor to open (after `menu: Art > Animation Clips...`). |
| `clipframes: <first> > <last>` | Drag across the Clip Editor's sheet with the real mouse, from one frame to another (by sheet frame), making them the picked clip's frames: `clipframes: 2 > 5`. One frame clicks it (`clipframes: 3`); `clipframes: Shift+6` Shift-clicks, stretching the clip to reach it. |
| `clipspeed: <fps>` | Type the picked clip's Speed into the Clip Editor (frames a second). Its looping is `click: clips:loop`. |
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

Targets: `panel:Inspector` (or `Hierarchy`, `Assets`, `Output`, `Profiler`; docked or floating), `float:Output` (the floating window a panel is in), `logo` (the griffin in the title bar), `scene`, `game`, `rewind` (the Rewind bar over the game while it plays), `player:2` (a player's window, or the Game tab for 1), `toolbar:Game.Play`
(a command id), `field:Health.Max`, `component:Health` (its header), `heading:Health` (the name on it), `fold:Health` (the arrow that folds it shut), `group:PlatformerMovement.Dash` (a subheading in its section) and `fold:PlatformerMovement.Dash` (the arrow that folds that), `remove:Body` (its remove button), `addall:Light` (the Add to All button on a component only some of the selection have), `need:PlatformerMovement` (the flag under a section saying what it needs and its entity lacks; `need:PlatformerMovement.Body` for one of several), `needadd:PlatformerMovement` (the button on that flag that adds it, or `needadd:PlatformerMovement.Collider`), `addlist` (the open Add Component list), `addlist:search`, `addlist:list`, `addlist:plan` (what Enter adds), `addlist:browse` and `addlist:item:Patrol` (a row), `label:Health.Max` (a field's name), `entity:Player`, `lock:Backdrop` (the lock button on a Hierarchy row), `eye:Paused` (its eye, which hides it in the Scene view), `prefab:Gem` (the package mark on a prefab copy's Hierarchy row), `prefabnote` (the Inspector's line saying which prefab the selection is a copy of), `override:Sprite.Tint` (the mark by a field the selected copy changed from its prefab), `handle:rotate` (the Scene view's rotate handle), `handle:top-right` (a scale handle, named as `scale:` names them, or a resize handle with those out, as `resize:` names them, or a distance's knob with those out, `handle:Light.Radius`, or a knob on the selection's path, named as `path:` names them, `handle:Mover.2`), `link:Lever > Bridge` (the label on the Scene view's arrow from one entity to another), `tiles:Rectangle` (a tool's button under the Scene view's tile palette while a tilemap is selected: `Brush`, `Rectangle` or `Fill`), `tiles:3` (a tile in the palette, by its number: `tiles:100` is the second tileset's first), `tiles:props` (a tileset's tab along the palette's top, by its image's name) `tiles:+` (the tab that adds a tileset), `tiles:layer:Walls Layer 2` (a layer's button under the tools, by its entity's name) and `tiles:layer:+` (the + Layer button), `menu:Game`, and
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
`painter:ideas`, `painter:options`, `painter:layout` and `painter:size`. In the Clip Editor: `clips` (the window), `clips:clip:Run` (a clip in the list, or `clips:clip:(own frames)`), `clips:frame:2` (a frame in the strip, by its sheet frame), `clips:sheet:2` (a frame on the sheet), `clips:sheet` (the sheet), `clips:settings` (Speed and Loops), `clips:speed`, `clips:loop`, `clips:note` (where the speed and looping come from), `clips:list`, `clips:preview`, `clips:frames`, `clips:play`, `clips:back`, `clips:forward`, `clips:position` (which frame is showing), `clips:add`, `clips:rename`, `clips:remove`, `clips:start`, `clips:buttons`, `clips:notice`, `clips:addanimation` and `clips:entity`. An image field's buttons in the Inspector:
`image:Sprite.Image:choose` and `image:Sprite.Image:paint`. Any field with a list (entities, prefabs, scenes, actions, layers, messages, images, sounds) has its list button, `pick:Switch.Targets`. A color field's swatch is `color:Sprite.Tint`, which opens its picker, whose parts are `color:picker` (all of it), `color:square`, `color:hue`, `color:alpha`, `color:hex`, `color:palette`, `color:swatches`, `color:recent` and `color:pick:#29ADFF` (a swatch in the palette or Recent). The Transform's 3D switch is `transform:3d` and the note naming what 2D hides `transform:hidden`; the filters are `filter:fields` (the Inspector's) and `filter:entities` (the Hierarchy's). The Profiler panel's parts are `profiler:graph`, `profiler:rate` (the frame rate over the graph), `profiler:garbage`, `profiler:costs` (what each script and step costs a frame), `profiler:counts`, `profiler:components`, `profiler:record` and `profiler:clear`. An asset's row in the Assets panel is `asset:Prefabs/Gem.prefab` (the folders it's in are opened).

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
code), `code:line:12`, `code:breakpoint:Speed * Time` (the strip left of the line numbers, on the line with that code:
clicking it puts a breakpoint there, or takes it off), and while they show, `code:completion`, `code:signature` and
`code:quickinfo`. While the game is stopped at a breakpoint in the script: `code:paused` (the panel under the code),
`code:values` (the values in scope), `code:value:this` (a value's row, or `code:value:this.Speed` for one inside
another), `code:expand:this` (the arrow that opens it), `code:continue` (the Continue button, also `code:resume`), and
`code:stepover`, `code:stepinto` and `code:stepout` (the step buttons).

Keep a demo to a few minutes: show the change working, the way someone would use it, and say why it matters.
