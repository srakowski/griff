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
- **Batteries included.** Sprites and sprite-sheet animation, text, 2D physics with triggers, input, sound and music, a follow camera, and Platformer and Top-down templates that are playable from the first second.
- **One click to ship.** Build Game produces a standalone folder with a `.exe`, ready to zip and upload.

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
| Cut / copy / paste entities | Ctrl+X / Ctrl+C / Ctrl+V |
| Build game | Ctrl+B |

In the **Scene view**:
- Scroll to zoom.
- Middle-drag, right-drag or Alt-drag to pan.
- Click to select; drag to move (hold Ctrl to snap).
- Press F to frame the selection.
- Arrow keys nudge the selection by half a unit (Shift for a tenth).

In the **Inspector**, drag any number's label sideways to scrub it (Shift for fine steps, Ctrl for coarse).

In the **Assets** panel:
- Drag an image into the Scene view to create a sprite.
- Drag any asset onto an Inspector text field to fill in its path.
- Double-click a scene to open it; right-click it to make it the start scene.

Games can switch scenes with `Game.LoadScene("Level2")`.

## Scripting

Scripts are plain C# classes in your project's `Assets` folder. **Entity → New Script** creates one, attaches it to the selected entity and opens it. Common namespaces (`System`, `System.Numerics`, `Griff`, `Griff.Runtime`, `Griff.Scenes`) are imported automatically.

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

What a behavior can use:
- **Lifecycle:** `Start`, `Update`, `OnCollision`, `OnTriggerEnter`, `OnTriggerExit`, `OnDestroy`.
- **Services:** `Transform`, `Time` (`Delta`, `Total`, `Scale`), `Input` (`IsDown`, `WasPressed`, `Horizontal`, `Vertical`, `MouseWorld`; keyboard, mouse and Xbox-style gamepads) and `Audio` (`Play`, `PlayMusic`).
- **Game:** `Spawn`, `Destroy`, `Find` and `Physics` (`OverlapPoint`, `OverlapBox`).

Built-in components:
- `Transform`, `Camera`, `Sprite` (single images or sprite sheets) and `Text`;
- `Animation`, which plays a range of sprite-sheet frames;
- `BoxCollider` and `Body`, for physics.

Saving a script while the game is playing applies it immediately. Public properties are kept, and `Start` runs again on the new version.

## Architecture

```
src/
  Griff                    Engine core: scenes, serialization, editing/undo, runtime, physics,
                           render lists, projects and templates. No UI dependencies.
  Griff.Scripting          Roslyn script compiler, hot reload, game exporter
  Griff.Graphics           OpenGL 3.3 sprite and text renderer
  Griff.Platform.Windows   WGL context, WPF game surface, input, audio, text rasterizing
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
- Physics uses axis-aligned boxes only; rotation is ignored, and there are no slopes or circles yet.
- Panels are resizable but not dockable.
- Text uses one built-in font.
