# Demos

Each `.demo` file is a short, narrated walkthrough of one feature or change. Griff plays it by driving its own
editor with real mouse and keyboard input, speaking the narration aloud, spotlighting what it talks about, and
showing a control bar to pause, change speed, skip or rewind between chapters, or exit.

```powershell
tools\demo.ps1 demos\single-file-builds.demo           # play it, narrated (move the mouse to pause)
tools\demo.ps1 demos\single-file-builds.demo -Check    # run it silently at speed; PASS or FAIL
tools\demo.ps1 -All -Check                             # check every demo: run this when features change
tools\demo.ps1 -Tour                                   # the full guided tour that builds Ember Hollow
```

Demos are kept current. A change that alters something a demo shows updates that demo in the same commit, and
`tools\demo.ps1 -All -Check` passes before it's committed.

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
| `project: <Template> [\| <Name>]` | A fresh project from a template: `Empty`, `Platformer`, `TopDown` or `Arcade`. |
| `open: <folder>` | A copy of an existing project (the original is never changed). |
| `scene: <name>` | Open a scene by name. |
| `chapter: <title> [\| <subtitle>]` | Start a chapter: a card, a dot on the control bar, a rewind point. |
| `say: <text>` | Narrate (spoken and captioned). |
| `menu: <Menu> > <Item>` | Click through the main menu, e.g. `menu: Game > Build Game`. |
| `create: <thing>` | Shorthand for `menu: Entity > Create <thing>`, e.g. `create: Platformer Player`. |
| `key: <shortcut>` | Press a shortcut, e.g. `key: Ctrl+Shift+B`, `key: F5`. |
| `select: <entity>` / `deselect` | Pick an entity in the Hierarchy, or nothing. |
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
| `tap: <Key>` / `hold: <Key> <seconds>` | Press a key in the running game, e.g. `hold: Right 1.5`, `tap: Space`. |
| `type: <text>` | Type into whatever has focus. |
| `wait: <seconds>` | Pause the demo. |
| `code: <file> [\| <title>]` / `hidecode` | Type a file (relative to the project) out in the code card. |
| `save` | Ctrl+S. |
| `build: standalone` / `build: small` | Game > Build Game (or Build Small Game) and wait for the exe. |

Targets: `panel:Inspector` (or `Hierarchy`, `Assets`, `Output`), `scene`, `game`, `toolbar:Game.Play`
(a command id), `field:Health.Max`, `component:Health`, `entity:Player`, `menu:Game`, and
`world:left,top,right,bottom` for a box in the Scene view. In the component catalog: `catalog:button` (the
Inspector's Catalog button), `catalog` (the window), `catalog:search`, `catalog:list`, `catalog:docs`,
`catalog:requires`, `catalog:workswith`, `catalog:neededby`, `catalog:properties`, `catalog:plan`, `catalog:add`,
`catalog:close`, `catalog:item:Body` (a row in the list), `catalog:link:Body` (a link in the details) and
`catalog:property:Jump Height`.

Keep a demo to a few minutes: show the change working, the way someone would use it, and say why it matters.
