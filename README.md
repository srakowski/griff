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
- **Batteries included.** Sprites and sprite-sheet animation with named clips, tilemaps you paint right in the Scene view, pathfinding around their walls, particles, prefabs, timers, coroutines, tweens with easing, messages between scripts, text in your own fonts, 2D physics with boxes, circles, bounce, triggers, collision layers, raycasts and moving platforms, a follow camera that stays inside the level, parallax backgrounds, top-down depth sorting, camera shake, debug drawing, save data, input, sound and music, menus and HUDs with clickable buttons and screen-pinned text, fades between scenes, and Platformer, Top-down and Arcade templates that are playable from the first second.
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
- Drop an image to create a sprite, or a prefab to place a copy of it.

**Tilemaps.** **Entity → Create Tilemap**, then set its **Image** to a tileset and its **Columns** and **Rows** to the tileset's layout. While a tilemap is selected, the Scene view paints:
- Drag to paint, Shift-drag to erase. Each stroke is one undo step.
- Pick a tile from the palette at the top left, with `[` and `]`, or Ctrl-click a painted tile.
- Paint past the left or top edge and the map grows that way.
- Press Esc (or select something else in the Hierarchy) to stop painting.

The map's tiles are also plain text in the Inspector, one row per line (`.` is empty, `0`–`9`, `a`–`z`, `A`–`Z` are tiles 0–61), so you can type a level out or paste one in. With **Solid** on, tiles are walls and floors, except:
- **Background Tiles** never collide: grass, windows, signs. List tile numbers and ranges, e.g. `10-15, 20`.
- **One Way Tiles** are platforms you jump up through and land on.

The Scene view outlines what collides: one-way runs get a dashed outline with a solid top edge.

**Menus and HUDs.** **Entity → Create Button** makes a clickable button: a background sprite, a label and a **Button** component. Set its **Action** and a menu needs no code at all: **LoadScene** (with **Scene**, e.g. `Game`), **RestartScene** or **Quit**, with a **Fade** (in seconds) to fade the screen out and back in around the switch. A **Shortcut** key (Enter for Play, Esc for Back) clicks it too. Add a **ScreenAnchor** to pin anything to an edge or corner of the screen, like a score in the top-left: it stays put whatever the window's shape, while the camera follows the player or shakes. The Scene view outlines buttons' clickable areas in blue. The Arcade template shows all of this: a main menu, a game, and a game over panel with Retry and Menu, fading between them. Its bullets use collision layers to fly out of the ship, and its enemy spawner is a coroutine.

**Cameras.** Add a **Follow** to the camera and set its **Target** (`Player`) and it follows with no code. **Smoothing** sets how quickly it catches up (0 sticks exactly), **Offset** shifts the view (say `0, 2` to see more above the hero), **Dead Zone** is a box the target can move about in without the camera moving (a tall one keeps a platformer's camera still through jumps), and **Look Ahead** leads the way the target is moving. Set **Stay Inside** to the name of the level's tilemap, a big box collider or a backdrop sprite, and the camera never shows past its edges. Follow works on anything, not just cameras: a pet that trails its owner. For top-down games, tick the camera's **Sort By Y** so whatever is lower on screen draws in front.

**Parallax.** Add a **Parallax** to a background entity and set its **Factor**: 1 moves like the rest of the world, 0 stays stuck to the camera like a distant sky, and in between is in between (far hills 0.2, near trees 0.7). It shifts everything the entity and its children draw. **Repeat X** and **Repeat Y** repeat its sprite to fill the view, so a short strip of hills never runs out. It's applied as the game is drawn, so the layer is exactly where its transform puts it when the camera is at the origin, and the Scene view shows it there.

**Prefabs.** Select an entity and choose **Entity → Save as Prefab** to save it (with its children) to `Assets/Prefabs`. Drag a prefab into the Scene view to place it, or spawn it from a script. Save as Prefab works while playing too, to keep a live entity you've tweaked.

In the **Inspector**, drag any number's label sideways to scrub it (Shift for fine steps, Ctrl for coarse).

In the **Assets** panel:
- Drag an image into the Scene view to create a sprite.
- Drag any asset onto an Inspector text field to fill in its path.
- Double-click a scene to open it; right-click it to make it the start scene.

Games can switch scenes with `Game.LoadScene("Level2")`. **Game → Clear Save Data** forgets what the game saved while playing in the editor.

## Scripting

Scripts are plain C# classes in your project's `Assets` folder. **Entity → New Script** creates one, attaches it to the selected entity and opens it. Common namespaces (`System`, `System.Collections`, `System.Numerics`, `Griff`, `Griff.Runtime`, `Griff.Scenes`) are imported automatically.

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

// Remember things between runs.
if (Score > Game.SaveData.GetInt("best"))
    Game.SaveData.Set("best", Score);
```

`Rand` gives random numbers for gameplay: `Range`, `Chance`, `Pick`, `Sign`, `Direction`, `InsideCircle`, `Color`, and `Seed` to make a run repeatable.

What a behavior can use:
- **Lifecycle:** `Start`, `Update`, `OnCollision`, `OnTriggerEnter`, `OnTriggerExit`, `OnClick` (the entity's button was clicked), `OnDestroy`.
- **Services:** `Transform`, `Time` (`Delta`, `Total`, `Scale`), `Input` (`IsDown`, `WasPressed`, `WasReleased`, `Horizontal`, `Vertical`, `MouseWorld`; keyboard, mouse and Xbox-style gamepads), `Audio` (`Play`, `PlayMusic`) and `Debug` (`Line`, `Ray`, `Box`, `Circle`, `Point`, `Path`, `Text`: drawn over the game for one frame, or for `seconds:`, with lines the same thickness however far the camera zooms; `Debug.Enabled = false` hides them all).
- **Messages:** `Game.Send("Name")` or `Game.Send("Name", data)` reaches every listener straight away and returns how many heard it. `On("Name", () => ...)` and `On<int>("Name", value => ...)` listen until the entity is destroyed or the script is reloaded (like coroutines, so listen in `Start`); they return a listener you can `Cancel()`, and `StopListening()` stops all of a behavior's. Listeners in a scene you leave stop too, unless their entity is kept with `KeepBetweenScenes`. `Game.On` makes one that doesn't belong to a behavior.
- **Timers:** `After(seconds, action)` and `Every(seconds, action)` return a timer you can `Cancel()`. They run on game time, so they pause and slow down with it. `Game.After` and `Game.Every` make timers that don't belong to an entity.
- **Coroutines:** `StartCoroutine(SomeMethod())` runs an `IEnumerator` method straight away up to its first `yield return`, then picks it up again once what it yielded is done: `null` (the next frame), `Wait(seconds)` (or just a number), `WaitUntil(() => ...)`, `WaitWhile(() => ...)`, a tween, a timer, another coroutine, or another `IEnumerator` method (run to the end first, like calling it). They run on game time like timers and return a coroutine you can `Stop()`; `StopCoroutines()` stops all of a behavior's. They stop when the entity is destroyed and when the script is reloaded, and since `Start` then runs again on the new version, a coroutine started in `Start` restarts running your new code. `Game.StartCoroutine` makes one that doesn't belong to a behavior.
- **Tweens:** `MoveTo`, `ScaleTo`, `RotateTo` and `FadeTo` (sprite, text and tilemap alpha) animate this entity. `Tween(seconds, t => ...)` animates anything. Each takes an optional curve from `Ease` (`Linear`, `InQuad`, `OutQuad` (the default), `InOutQuad`, `InCubic`, `OutCubic`, `InOutCubic`, `InOutSine`, `InBack`, `OutBack`, `OutElastic`, `OutBounce`) and returns a tween you can `Cancel()` or chain with `.Then(...)`. A new `MoveTo` takes over from an unfinished one. Like timers, tweens run on game time and stop if their entity is destroyed; `Game.MoveTo(entity, ...)` and friends work on any entity.
- **Game:** `Spawn` (an entity, or a prefab by name), `Destroy`, `Find` and `FindAll` (by name, or `Find<T>()` and `FindAll<T>()` by component type), `LoadScene` and `ReloadScene` (optionally with a `fade:` in seconds and a `color:`; fades use real time, so they work from a paused menu, and `IsChangingScene` is true until the switch), `KeepBetweenScenes` (carry an entity into the next scenes, still running; returning to its original scene doesn't duplicate it), `Quit`, `ShakeCamera`, `SaveData` (`GetInt`, `GetFloat`, `GetBool`, `GetString`, `Set`, `Delete`, `Clear`) and `Physics` (`Raycast`, `OverlapPoint`, `OverlapBox`, `OverlapCircle`; each takes `layers:` to only find colliders on those layers).

Built-in components:
- `Transform`, `Camera` (with `Shake`), `Sprite` (single images or sprite sheets) and `Text` (with `Align` and a `Font`: a `.ttf`/`.otf` file in Assets, which ships with the game, or an installed font's name);
- `Animation`, which plays a range of sprite-sheet frames, or named **Clips** (`Idle 0-3, Run 4-9 @12, Die 11-15 once`: a name, frames, and optionally a speed and `once`) switched with `Play("Run")`, which carries on if that clip is already playing. Its **Clip** picks the one the game starts with;
- `Tilemap`, a painted grid of tiles from one tileset (`GetTile` and `SetTile` from scripts), with background and one-way tiles. `FindPath(from, to)` finds the shortest way between two world positions around its solid tiles (background and one-way tiles can be walked through), as waypoints at the corners, moving diagonally but never cutting a wall's corner (pass `diagonal: false` for four directions);
- `ParticleEmitter`, for continuous effects or one-shot bursts (`Burst`; `BurstOnStart` with `DestroyWhenFinished` makes a self-cleaning effect prefab);
- `Button`, a clickable area that lightens under the mouse and darkens while held, with a no-code **Action** (and **Fade**), a **Shortcut** key, `OnClick` for behaviors on its entity and a `Clicked` event for anyone else. Put the label `Text` on the button's own entity so it's shaded with it;
- `Follow`, which moves its entity after a **Target**, smoothly, with an offset, dead zone, look-ahead and an area to **Stay Inside** (see Cameras above). Scripts can set `TargetEntity` to follow something directly. `Camera` has **Sort By Y** for top-down games;
- `Parallax`, which scrolls a background layer at a fraction of the world's speed and can repeat its sprite across the view;
- `ScreenAnchor`, which pins its entity to one of nine points on the screen plus an **Offset**, for HUDs and menus. It's applied while the game runs, so the Scene view shows the entity where its transform puts it;
- `BoxCollider`, `CircleCollider` and `Body`, for physics. Boxes and circles collide in any combination. `GetComponent<Collider>()` finds either kind. A body's `Bounciness` (0 to 1) makes it bounce off what it hits, settling once the bounces get small. `Ground` is what it's standing on; when that moves (a platform moved by a script or a tween) the body rides along with it. Give colliders a **Layer** (`Player`, `Enemy`, `Bullet`; empty is `Default`) and list what they pass through in **Ignore Layers**, e.g. `Player, Bullet` on a bullet: ignored pairs don't block each other, collide or trigger, and only one side has to list the other. Tilemaps have both too.

Saving a script while the game is playing applies it immediately. Public properties are kept, and `Start` runs again on the new version.

Rewinding restores everything a scene file holds. Things that only exist while running (timers, coroutines, message listeners, tweens, live particles, camera shake, a scene fade, debug shapes) start fresh from the moment you resume, the same way private fields do.

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
- Physics uses axis-aligned boxes and circles. Rotation is ignored, there are no slopes, and circle bodies slide rather than roll.
- Tilemaps use one tileset of up to 62 tiles.
- Panels are resizable but not dockable.
