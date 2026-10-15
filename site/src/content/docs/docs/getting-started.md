---
title: Getting started
description: Run Griff's editor, make a project from a template and play it.
---

Griff runs on 64-bit Windows.

## Get the editor

Builds will be on the [releases page](https://github.com/srakowski/griff/releases). Until there's one there, build the editor from source: it takes the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and a minute.

```sh
git clone https://github.com/srakowski/griff.git
cd griff
dotnet run --project src/Griff.Editor
```

See [Building Griff](/docs/building/) for the tests and the rest.

## Make a project

1. **File → New Project** (Ctrl+Shift+N).
2. Give it a name and a folder, and pick a template: the Platformer is a good first one. Each template is a small game that plays from the first second.
3. **Create**. The project opens on its start scene.

## Play it

Press **F5** to play in the **Game** tab, **F6** to pause, **F10** to step a frame and **Shift+F5** to stop. While it plays you can select things in the Hierarchy and change them in the Inspector; Stop puts everything back, and **Keep Play Changes** (Ctrl+K) keeps what you changed on the selected entity.

While playing, Griff keeps the last 20 seconds: pause and drag the **Rewind** bar back to see what happened.

## Change it

- Select an entity in the **Hierarchy** or the **Scene view**, and change its components in the **Inspector**. Every change is one undo step (Ctrl+Z, Ctrl+Y).
- The **Entity** menu makes ready-made things (a platformer player, a camera, a button, a tilemap, a crate...), and **Entity → New Script** makes a C# script, attaches it to the selected entity and opens it. Save it, and the running game picks it up at once.
- Select a tilemap to paint it in the Scene view: **B** brush, **R** rectangle, **G** fill, Shift to erase.

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

## Ship it

**Game → Build Game** (Ctrl+B) makes a single `.exe` that carries everything, .NET included: zip it and upload it to your jam's page. **Build Small Game** makes a 4 MB `.exe` for players who have the .NET Desktop Runtime.

Next: [the editor](/docs/editor/) goes through everything it can do, and [scenes and components](/docs/scenes-and-components/) what a game is made of.
