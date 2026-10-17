<!--
  What changed in each Griff release, in plain words, newest first. The Releases page shows this file.
  "Unreleased" collects the changes since the last release; tools/changelog.ps1 -Version <x> turns it into that
  version's entry (dated today) and starts a new empty Unreleased. Format: "## Unreleased" or "## <version> - <date>",
  then "- " bullets; `code` is shown as code and **bold** as bold.
-->

## Unreleased

- Camera Controls pans with four new built-in actions, **PanUp**, **PanDown**, **PanLeft** and **PanRight** (WASD, the arrows and the D-pad as before), so **Game > Controls** and players can rebind them, and a game can take W or S for something else.
- A button's **Shortcut Action** clicks it with an input action instead of a fixed key, so a menu key shows in **Game > Controls** and players can rebind it.
- A **Bar** can be drawn with pictures: its **Fill Image** is revealed as it fills, in its **Direction**, cut off rather than squeezed, over a **Background Image** that can be a nine-slice. Its colors tint them.
- A script spawned during the game no longer stops it with "The behavior isn't running in a game yet" when a **Tooltip**, **Text**, **Counter**, **Bar** or wire shows one of its outputs that uses `Game`: a script's outputs are read once it has started, from the next step. A **Tooltip** now reads its output only while it's the one hovered.

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
