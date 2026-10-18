---
title: "Scripting"
description: "Scripts are plain C# classes: the script editor, breakpoints, components you describe for the Inspector, and what a behavior can use."
---

Scripts are plain C# classes in your project's `Assets` folder. **Entity → New Script** creates one, attaches it to the selected entity and opens it. Common namespaces (`System`, `System.Collections`, `System.Numerics`, `Griff`, `Griff.Data`, `Griff.Input`, `Griff.Runtime`, `Griff.Scenes`, `Griff.Simulations`) are imported automatically, and so is the namespace of each genre your project uses: a project whose scenes or prefabs have a Match Board gets `Griff.Modules.Match3`, so a script names `MatchBoard` as it does `Sprite`. A genre the project doesn't use yet is a `using` away (`using Griff.Modules.Strategy;`): every genre's types are there to compile against, and a script that names one brings that genre into the game, and into its export.

## The script editor

Scripts open in Griff's own code editor, as tabs next to Scene and Game: from New Script, by double-clicking a `.cs` file in Assets, with the code button on a script component's header in the Inspector (**Edit Script**), or by double-clicking a script error in Output. It's C# with the compiler behind it, so it knows Griff's whole API and your other scripts, unsaved changes included:

- Colors by meaning: properties, methods, types, parameters and fields each have their own color. Code is set in JetBrains Mono, which ships with Griff (SIL Open Font License; see `Themes/Fonts` and Licenses).
- Completion as you type, with each item's signature and documentation; signature help inside calls (Up and Down pick an overload); hover anything for its documentation, or for the compiler's message.
- Errors and warnings as you type: underlined where they are, counted in the header (click for the list), and in red in the line numbers.
- **Ctrl+S** saves, compiles and swaps the change into the scene, and into the game if it's playing. Playing saves open scripts first.
- **The component panel** beside the code (the **Component** button) shows the component the script declares: what it does, what it needs, and each property with its kind, starting value and description, plus its value on an entity in the scene that uses it (live while playing), editable right there. Click a property's name to go to it in the code; the property the caret is in lights up. New properties show up as soon as you type them, waiting for a save.
- F12 or Ctrl+click goes to a definition, Shift+F12 lists every use, F2 renames across all scripts, Shift+Alt+F formats, Ctrl+F finds and Ctrl+H replaces (with case, whole-word and regular-expression options), Ctrl+G goes to a line, Ctrl+/ comments lines, Ctrl+D duplicates them, Ctrl+Shift+L deletes them, Alt+Up and Alt+Down move them, and Ctrl+wheel zooms. Brackets and quotes close themselves, `}` lines up with its `{`, uses of the name at the caret are highlighted, blocks fold (**Expand All** and **Collapse All** above the code open or fold every one), and indent guides follow the nesting.
- Snippets: `prop` (a property for the Inspector), `update`, `start`, `trigger` and `coroutine` in a class; `if`, `for`, `foreach` and `while` in a method. Type the name and press Tab.
- **Breakpoints.** Click beside a line number (a red dot), or press F9 on the line, and the game pauses when a script gets there, in the middle of its frame (a breakpoint on a line without code goes on the next one with some). The script opens at that line with a yellow arrow, and below the code are the values in scope: the object the code runs on (`this`: the script's own fields, then what it builds on, its Entity, Transform and the rest) and the method's parameters and the variables set by that line, each to open up (an object's fields and properties, a list's items, a dictionary's entries). Hovering a variable in the code shows its value too. **Continue** (F5 or F6, or the panel's button) lets the script carry on from that line; Stop leaves the frame, and rewinding goes back to before it, from any step. Breakpoints work while the game runs, follow their code as lines are added or taken out above them (unsaved edits too: the game stops at the code as it was built), are kept with the project, and **Debug → Remove All Breakpoints** (Ctrl+Shift+F9) clears them. Agents set them and read the values through the Debugger (see [Agents](/docs/agents/)).
- **Stepping.** Stopped at a line, **Step Over** (F10) runs it and stops at the next line of the same method, without stopping in what it calls (a breakpoint in there still stops it). **Step Into** (F11) stops at the next line of script that runs: inside a method the line calls, in this script or another (which opens at that line), or in a script Griff runs for it, such as a spawned thing's Start; a call into Griff that runs no script is stepped over. **Step Out** (Shift+F11) runs the rest of the method and stops at the next line of the script that called it. A method Griff called (Update, a collision, a message, a coroutine reaching its `yield`) returns into Griff: a step off its end goes on to the next line of any script that runs, which may be the same script on the next entity, and if none runs before the frame ends, the game pauses there, after the frame, with the panel saying so; Step Into from there (or from any pause) plays the next frame up to the first line of script it runs. Recursion, lambdas called by .NET (a list's `ForEach`), catch blocks and crowds step as you'd expect, and the panel under the code has Continue, Over, Into and Out, with the Debug menu and their shortcuts working while you look round the editor.
- **Exceptions.** A script that throws an exception the game doesn't catch stops the game at the line that threw, in the middle of its frame and before anything unwinds, as a breakpoint does: the line gets a red band, the panel under the code says what was thrown, and the values in scope there are below it (the line's own variables as they were when it threw, and `this`). A call into Griff that throws (a bad prefab name, say) stops at the line of script that called it; a crowd script running on another core stops at its own line with its own values; a coroutine stops at the line it was on, its variables read from the routine running it. The error goes to Output once. **Continue** (F5) lets the exception end that frame (the rest of it doesn't run, as without the debugger) and the game plays on, stopping again if it throws again; a step ends the frame and pauses after it (Step Into then plays the next frame to its first line); Stop leaves it. **Debug → Rewind to Before the Exception** (also the panel's button, and a tick on the rewind bar) goes back to the moment the step that threw began, paused there: put a breakpoint before the line and Continue (or Step Frame) to watch it happen again. It's there until the game stops, while that moment is on the rewind timeline. To make that moment exact, play mode remembers the game between every two steps (copied out as a snapshot is, kept only if the next step throws); a scene too big to copy out in a quarter of a millisecond isn't remembered every step, and rewinds to the last snapshot before it instead. **Debug → Pause on Exceptions** (on unless turned off, kept between sessions) turns it all off: an exception then ends the frame and pauses the game, with the error in Output, as in an exported game. The editor's build of the scripts has guards for this: each stretch of a method's code sits in a `try` whose filter notes an exception coming up through it with the variables in scope there and catches nothing, so a script's own `catch` still catches, nothing changes until something is thrown, and an exported game has none.
- Breakpoints cost nothing until the game gets to one. The editor builds scripts with a probe at the start of each line, a test of a switch of that line's own that a breakpoint turns on, so setting one rebuilds or restarts nothing, and a line without one only reads a switch that's off (about 3% of a tight loop of one-line sums, in the Debug-built code the editor runs anyway). An exported game has no probes at all. Stopping changes nothing in the game: while it's held, what the editor does doesn't draw on the game's random numbers, reading values runs none of its code (a script's fields are read as they are), and the time it stood still isn't time it owes. With a breakpoint set, a crowd's scripts (`[Crowd]`) run on the game's thread, where one can stop them, rather than across the cores; they play out the same either way, also while stepping through them. While a step is under way every line's switch is on, and each line asks whether the step ends there (by where on the stack it runs, which tells a call from a return); once it's over, only breakpoints' switches stay on.

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

Scripts read the game's tables with `Tables`: `float damage = Tables.Get("Spells", "Fireball")["Damage"];`, or a class of their own filled from a row, `Tables.Get<Spell>("Spells", "Fireball")` (see [Tables of game data](/docs/data-tables/#tables-in-scripts)). A `SimulationScript` plays a balance simulation's runs, and `Game.Simulate("Balance")` plays one from a game (see [Script simulations](/docs/data-tables/#script-simulations)).

Describe a script for the component catalog and the Inspector's tooltips with a `///` summary over the class and its properties (say what units a number is in), and say how it fits with other components using attributes:

```csharp
/// <summary>Walks toward the player and explodes next to them.</summary>
[Category("Enemies")]                                  // where the catalog lists it (default: Scripts)
[Requires(typeof(Body), Note = "moves by velocity")]   // Add Component adds a Body along with it
[Requires(typeof(Collider))]                           // a base type: any kind of collider will do
[WorksWith(typeof(Health), Note = "so it can be shot")]
public class Creeper : Behavior
{
    /// <summary>How close it gets before exploding, in world units.</summary>
    [Distance]                                         // drawn as a circle around it in the Scene view, with a handle
    public float FuseRange { get; set; } = 1.5f;

    /// <summary>What it walks toward.</summary>
    [EntityName]                                       // picked from the scene's entities in the Inspector (Many = true for a list)
    public string? Target { get; set; } = "Player";

    /// <summary>Flash before exploding.</summary>
    [Group("Warning")]                                 // a subheading in the Inspector, over this and the properties after it
    public bool Flash { get; set; } = true;

    /// <summary>Flashes a second.</summary>
    [ShownWhen(nameof(Flash))]                         // faded in the Inspector while Flash is off
    public float FlashRate { get; set; } = 4;

    /// <summary>The blast's damage, e.g. "3d6".</summary>
    [Dice]                                             // built in the Inspector: how many dice, their sides, what's added
    public string? Blast { get; set; } = "3d6";

    /// <summary>Which way it runs from the light.</summary>
    [Direction]                                        // a compass in the Inspector, an arrow to turn in the Scene view
    public Vector2 Flee { get; set; } = -Vector2.UnitX;

    /// <summary>An item that defuses it, e.g. "Wire Cutters".</summary>
    [Names(NameKind.Item)]                             // picked from the items the game gives (NameKind.Flag, Prefab, Message...)
    public string? DefusedBy { get; set; }
}
```

Other fields with editors of their own are marked the same way: `[Points]` (a list of "x y" points), `[TileNumbers]`, `[TileAnimations]`, `[AutoTiles]` and `[Tilesets]` (tiles picked off the tilemap's tilesets), `[Clips]`, `[ChoiceOf(nameof(Choices))]` (one of what another property of it lists), `[PrefabCounts]` (prefabs and how many), `[OutputFormat(nameof(Output))]` (text showing an output), `[Format("{0} the price", Preview = nameof(ReadsAs))]` (text with holes, with a method that says how it reads now), `[NumberFormat]`, `[NameList]` (names in order, as text or a list), `[LetterCounts("ABC")]`, `[ItemCounts]` and `[ItemUses]` (an inventory's), `[TileCounts]`, `[ActionValue(nameof(Do))]` (the value the action another property names takes), `[DialogueLines]`, `[TableSets(nameof(Table))]`, `[Offset]` (a point from the entity, with a knob in the Scene view) and `[Resizable]` (a size, with resize handles). `[Names(NameKind.Saved)]` picks a name in the save data (`Defines = true` on a field that saves under it), and `[Names(...)]` on an action's value says what it names (`void Unlock([Names(NameKind.Flag)] string flag)`), so a trigger's **With** offers those. The editor's guide says what each looks like.

A script's fields get good editors without any of these: a list of strings is rows to add, remove and reorder (numbers or colors when they all are), and one whose lines are names with values (`"Sword: 30"`) a name and its value a row, a dictionary; an enum of many choices (a `Key`) is searched for by typing; a `Quaternion` shows as angles; a string called **Font**, **Prefab**, **Scene** or **Message**, or ending in one (`NextScene`, `WinMessage`), picks from the project's; a `Vector2` called **Direction** or ending in it gets the compass. Mark a list `[Pairs]` (or `[Pairs(Separator = "=", Values = CellKind.Number)]`) or `[ListRows(CellKind.Color)]` to say what it holds while it's still empty. And when typing is right for a string, say why with `[PlainText("what the shopkeeper says")]`: it stays a plain box (a list's lines one block of text), and `component_describe` tells agents it's free text. See [Every field has an editor](/docs/editor/#every-field-has-an-editor).

Scenes keep what was set in them when a script changes shape. Rename a class or a property and say what it was called with `[FormerName]`: scenes saved under the old name still load, and save under the new one. It goes on an `[Action]` method or an `[Output]` too: a trigger, button, card, counter, wire or condition that still names the old action or output keeps working. A name that resolves to nothing (a trigger doing an action nothing it's done on has, a counter reading an output that isn't there, a wire's input) is flagged in the Inspector and in `scene_tree`, reported when a game is loaded and said in Output as the game starts, instead of silently doing nothing. When what a value means changes, raise the component's `[ComponentVersion]` and add an upgrade step from the version before, which older scenes run through as they load (steps see properties under their current names). Values a component has no property for (one you removed, or from a newer version of it) are kept and written back, so loading and saving loses nothing. Griff's own components follow the same rules.

```csharp
[FormerName("Creeper")]                                // was class Creeper: scenes that use it still load
[ComponentVersion(2)]                                  // version 1 measured the fuse in seconds; 2 in frames
public class Bomber : Behavior
{
    [FormerName("FuseTime")]                           // was FuseTime
    public int FuseFrames { get; set; } = 90;

    [UpgradeFrom(1)]                                   // brings version 1 data up to 2
    static void SecondsToFrames(SavedData data) => data.Set("FuseFrames", (int)(data.Get<float>("FuseFrames") * 60));
}
```

A few more things scripts often do:

```csharp
// Spawn a prefab (Assets/Prefabs/Bullet.prefab) where the player is.
var bullet = Game.Spawn("Bullet", new Vector2(Transform.Position.X, Transform.Position.Y));

// A prefab your data names (a field you made) may be missing: TrySpawn warns once and gives null, instead of stopping the game.
var spark = Game.TrySpawn(Effect, this, "its Effect", new Vector2(Transform.Position.X, Transform.Position.Y));

// Do something later, or repeatedly. Timers belong to this entity and stop if it's destroyed.
After(0.5f, () => Game.Destroy(bullet));
Every(2f, () => Game.Spawn("Enemy", new Vector2(10, 0)));

// Is there ground just below? Ignore our own collider.
var below = Game.Physics.Raycast(new Vector2(Transform.Position.X, Transform.Position.Y), -Vector2.UnitY, 0.6f, ignore: Entity);

// Juice: a burst of particles and a camera kick.
GetComponent<ParticleEmitter>()?.Burst(20);
Game.ShakeCamera(0.3f, 0.2f);

// Tweens: pop in, glide somewhere, then fade out and disappear.
Transform.Scale = new Vector3(0, 0, 1);
ScaleTo(Vector2.One, 0.4f, Ease.OutBack);
MoveTo(new Vector2(4, 2), 1f).Then(() => FadeTo(0, 0.3f).Then(() => Game.Destroy(Entity!)));

// Find things. Every copy of a prefab shares its name, so Game.Find("Player") takes this machine's player in a network game
// and one in the screen's minigame first; scripts are found by type.
foreach (var enemy in Game.FindAll<Enemy>())
    enemy.Hit();
Game.Find<ScoreCounter>()?.Add(1);

// Random spawn points, loot rolls and variety. Rand.Seed(42) makes a run repeat (particles and camera shake draw from Rand too).
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
// A module's drawings to debug with, by name (a fighting game's hitboxes); a system of your own can make one (IDebugDrawing).
Game.ShowDebugDrawing("Hitboxes", true);

// Line of sight that sees through other enemies: only walls (the Default layer) and the player block it.
var seen = Game.Physics.Raycast(eye, toPlayer, 10, ignore: Entity, layers: "Default, Player");

// Game flow: start over after dying, keep the music playing from scene to scene, quit from a menu.
Game.ReloadScene(fade: 0.4f);
Game.KeepBetweenScenes(Entity!);   // gives it a Keep Between Scenes

// Between scenes and genres: through to the cave arriving at its mouth; a minigame played on top, and how it went.
Game.LoadScene("Cave", fade: 0.4f, color: null, arriveAt: "Cave Mouth");
Game.PlayMinigame("Lockpick", from: Entity);
On<int>("Minigame Won", score => Game.Do(Game.Find("Gate")!, "Open"));
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
Game.Pause(Game.Find("Arcade")!);   // hold a minigame still while the rest runs on (Game.Resume starts it again)
if (Entity.IsPaused) return;        // this entity is in a paused group
GetComponent<Panel>()!.Opened += _ => Audio.Play("Sounds/open.wav");

// Show the player what to press, on the keyboard or the controller they're using.
hint.Value = $"Press {Input.PromptFor("Jump")} to jump";   // or just "Press {Jump} to jump"
keys.Value = Input.AllFor("DropTool");   // every key, "Esc / Right Click", as "{DropTool:all}"; Input.SlotFor("PanUp", 1) the second
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
On<int>("Beat", beat => { if (beat % 2 == 0) Game.Flash(Entity); });   // a Beat Clock's beat
if (Input.WasPressed("Jump") && Math.Abs(Game.Find<BeatClock>()!.SecondsFromBeat) < 0.1f) Combo++;
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

`SaveData` writes each change to disk at once, off the game's thread (setting a value every frame costs the frame nothing; what changes while one write is under way goes in the next), and the game waits for the last write as it stops. A write that fails, a full disk or a file another program holds, is said once in Output (`Game.Problems`) and the game goes on; the next change tries again.

`Rand` gives random numbers for gameplay: `Range`, `Chance`, `Pick`, `Sign`, `Direction`, `InsideCircle`, `Color`, and `Seed` to make a run repeatable. Games one running editor or player makes all start from the same numbers unless the project turns on **Game → New Random Numbers Each Game**.

What a behavior can use:
- **Lifecycle:** `Start`, `Update` (every step of the game: 60 times a second whatever the screen's frame rate, so read the controls and move things here; the screen is drawn between steps, so it moves smoothly), `FixedUpdate` (every step just before bodies move, and only while game time moves: not while paused, at `Time.Scale` 0 or in a hit stop, when physics stops too; push bodies here), `LateUpdate` (every step once everything else has moved: physics, pickups and damage, cameras following their targets; screen anchors are placed after it, so a camera of your own moved here keeps the HUD pinned), `OnCollision`, `OnTriggerEnter`, `OnTriggerExit`, `OnClick` (the entity's button was clicked), `OnDamage` (its Health was hurt: how much, and by what), `OnDeath` (its Health reached zero, just before it's destroyed), `OnPickup` (it collected a Pickup), `OnDestroy`; and `ClaimsBack`, overridden to have Back (Esc and B) while it's true (`protected override bool ClaimsBack => tool is not null;`), so a panel doesn't open on it, or close if the game goes on behind it, and the script puts the tool down first (see [Pause menus and panels](/docs/ui/#pause-menus-and-panels)). A press a panel acts on (the Esc that closes it, its Open On key) is used up: `Input.WasPressed` doesn't see it in that frame.
- **Crowds:** a script marked `[Crowd]` changes nothing but its own entity in its `Update`, `FixedUpdate` and `LateUpdate` (its own values, its entity's transform and components), so the game runs a crowd of them together, spread across the computer's cores, rather than one after another: thousands of bunnies, boids or bullets. Scripts still run in hierarchy order, a crowd (scripts of one crowd type next to each other in it) all at once at the place of its first: what comes before it has run and what comes after hasn't, and one inside another entity runs after the rest of its crowd, on the game's thread. A crowd script reads its own entity, the game's time and controls, and whatever nothing changes meanwhile (not another of its crowd, which may be moving at that moment); `Rand` gives each script of a crowd numbers of its own, from a seed the crowd takes from the game's numbers, so a crowd plays out the same on any number of cores and a seeded run repeats. What would change something else throws, saying so: spawning, destroying, messages, timers, coroutines, tweens, sounds, debug drawing, flashes, saving, adding or removing components, enabling or disabling entities and changing scenes. Do those from a script that isn't a crowd one, or in `Start`, which runs as any script's does. `Game.WorkerCount` says how many threads share a game's work (one for each logical processor, up to 16, to start; 1 runs everything on the game's thread): it's the game's own, set by whatever runs it, so another game in the same process (Play with Players, a simulation) keeps its own.
- **Services:** `Transform` (with `Teleport(position)`, to move it somewhere new without it being drawn gliding there), `Time` (`Delta`, the step's game time; `Total`, `Scale` (above 1, each step covers more game time, so what goes a step at a time is coarser); `FastForward`, fast-forward that stays exact: 16 takes sixteen steps of the usual length for each one, so a sim plays out step for step as at normal speed, only sooner, up to what the machine keeps up with (it runs slower rather than skipping steps), with `UnscaledDelta` still real time for the camera and menus; from 1 to 64, and nothing in a network game (see [Game speed](/docs/ui/#game-speed)); `Frame`, the steps taken; `StepRate`, steps a second, the project's Step Rate unless a script changes it, and `FixedDelta`, a step's length; `Blend`, how far the frame being drawn is between the last two steps; `DrawnFrames` and `RealTotal`, the frames the screen has shown and the real time they took, for a frame rate), `Input` (`IsDown`, `WasPressed`, `WasReleased`, `Horizontal`, `Vertical`, `Move`, `MouseWorld`, `Scroll` (mouse wheel notches this step), `Touches` (every finger on a touch screen, each with its `Id`, where it is in the world and whether it went down or was lifted this step; the first is the mouse too); keyboard, mouse and Xbox-style gamepads, with `LeftStick`, `RightStick`, `LeftTrigger` and `RightTrigger` (also buttons, `GamepadButton.RightTrigger`, when pulled past halfway), `IsGamepadConnected`, `UsingGamepad` (for showing "Press A" rather than "Press Space") and `Rumble`; `AimFrom(position)`, the right stick or towards the mouse; named actions: `IsDown("Fire")`, `WasPressed("Jump")`, `WasPressed("Jump", buffer: 0.12f)` (a press in the last moment that hasn't been used yet), `WasReleased`, with `Bind` to set an action's keys and buttons or add a new one, and `BindingsOf` to show them. `Rebind` is for the player's own choice from a controls screen: it's saved, comes back next time and wins over `Bind`; `ResetBindings` forgets it, and `AnyPressed` is whatever was just pressed. Actions are case-insensitive; a misspelled one says which exist. `TypedText` and `TypeInto` are for typing names; `PromptFor("Jump")` is what to show for an action on the device in use, "Space" or "A", and `InputPrompts.Name` names any key or button), `Audio` (`Play` (optionally with a `pan` from -1, left, to 1, right, and a `pitch`: 2 is an octave up), `PlayMusic`, `CrossfadeMusic`, `FadeOutMusic`, `StopMusic`, `PlayMusicFrom` (music started part way in, to carry a song on where it was), `Loop` (a sound that repeats until stopped, whose `Volume`, `Pan` and `Pitch` can change as it plays), `Music` (what's playing; none once music that doesn't loop has played to its end), `MusicTime` (seconds of it heard, allowing for the sound card's delay), `MusicLength` and `MusicTempo` (see [Keeping the beat](/docs/audio/#keeping-the-beat)), and `Volume`, `MusicVolume`, `SoundVolume` and `Muted` for an options menu; the same sound started several times in one frame plays once) and `Debug` (`Line`, `Ray`, `Box`, `Circle`, `Point`, `Path`, `Text`: drawn over the game for one frame, or for `seconds:`, with lines the same thickness however far the camera zooms; `Debug.Enabled = false` hides them all).
- **Players at one computer:** a `PlayedBy` (`Player`) gives an entity to one of them; `Game.InputOf(entity)` is then that player's keys or controller (a `LocalPlayerInput`), and `Game.LocalPlayers` says who plays with what: `Playing` (how many the scene has), `Input(player)`, `DeviceOf(player, playing)`, `Describe(player, playing)`, `Choose(player, PlayerDevice.Gamepad1)`, `Swap(1, 2, playing)` and `ResetChoices()`. The machine's `InputState` keeps controllers apart: `GamepadCount`, `IsDown(pad, button)`, `LeftStickOf(pad)`, `Rumble(pad, strength, seconds)`, and `HoldGamepadButton(pad, button, down)` to play one from a test.
- **Multiplayer:** `Game.Network` (the game's `INetwork` service, also `Game.Services.Get<INetwork>()`) hosts and joins network games and says who's playing, and everything it says has an answer offline too (no players, everything yours, a message just sent here), so there's nothing to check first. `Host()`, `HostServer()` (a dedicated server), `Join("192.168.1.5")` (or `Join("")` for the first game on the local network) and `Leave()`; `Role`, `IsOnline`, `IsHost`, `IsClient`, `IsConnecting` and `Status` (kept after a game ends, saying why); `Players`, `LocalPlayer` (each a `NetPlayer` with `Id`, `Name`, `Team`, `Color`, `Score`, `Ping`, `IsHost`, `IsLocal` and their `Avatar`), `RemotePlayers` (on the host, the others, each with their `Input`), `PlayerJoined`, `PlayerLeft` and `Ended` events, and the messages "PlayerJoined" and "PlayerLeft". `Network.Send("Chat", "gg")` reaches every machine as a message (data can be a number, text, a vector, a color, an entity, a player or a list of them), with `Network.Sender` saying who sent it. `OwnerOf(entity)` and `IsMine(entity)` say whose something is, `AddScore` and `TeamNames()` keep the score and the teams, `PlayerName` is remembered, and `FakeLag` pretends a bad connection. A game plays over the network only when what runs it says so (`OverNetwork`): the player and the editor's Play do, so a game hosted anywhere else (a test, a simulation, a tool) stays on this machine, hosting on 127.0.0.1 with no firewall question. A game has a network session only while it's networked: `Network.Session` (a `NetSession`, null offline) also has `SendToHost` and `SendTo(player, ...)` to reach one machine, the host's `Respawn(player)`, `SetTeam` and `Kick`, and `Traffic`. Scripts run on the host; their `Input` is the keys of whoever plays their entity (`Game.InputOf(entity)` for any entity), so a script on a player's character answers to that player.
- **Messages:** `Game.Send("Name")` or `Game.Send("Name", data)` reaches every listener straight away and returns how many heard it. `On("Name", () => ...)` and `On<int>("Name", value => ...)` listen until the entity is destroyed or the script is reloaded (like coroutines, so listen in `Start`); they return a listener you can `Cancel()`, and `StopListening()` stops all of a behavior's. Listeners in a scene you leave stop too, unless their entity is kept with `KeepBetweenScenes`. `Game.On` makes one that doesn't belong to a behavior (pass an entity as its `owner` to make it that entity's). `Game.Send("Name", data, from: Entity)` says who sent it, for a **Trigger**'s **From** (and a **Message Scope** on the sender or something it's inside keeps it to part of the scene, as Griff's own components' messages are); `Game.SendTo(entity, "Name", data, children: true)` reaches only that entity (and everything inside it). A listener with no entity of its own hears only what's sent to everyone.
- **Actions:** `Game.Do(entity, "Damage", 2)` (or `"2"`, or with no value) does an action on an entity as a **Trigger** would: every component of it with an action called that does it (`"Health.Damage"` names one component's). A script's own methods marked `[Action]` are actions too, found like its properties; see Joining in below.
- **Timers:** `After(seconds, action)` and `Every(seconds, action)` return a timer you can `Cancel()`. They run on game time, so they pause and slow down with it. `Game.After` and `Game.Every` make timers that don't belong to an entity.
- **Coroutines:** `StartCoroutine(SomeMethod())` runs an `IEnumerator` method straight away up to its first `yield return`, then picks it up again once what it yielded is done: `null` (the next frame), `Wait(seconds)` (or just a number), `WaitUntil(() => ...)`, `WaitWhile(() => ...)`, a tween, a timer, another coroutine, or another `IEnumerator` method (run to the end first, like calling it). They run on game time like timers and return a coroutine you can `Stop()`; `StopCoroutines()` stops all of a behavior's. They stop when the entity is destroyed and when the script is reloaded, and since `Start` then runs again on the new version, a coroutine started in `Start` restarts running your new code. `Game.StartCoroutine` makes one that doesn't belong to a behavior.
- **Tweens:** `MoveTo`, `ScaleTo`, `RotateTo` and `FadeTo` (sprite, text and tilemap alpha) animate this entity. `Tween(seconds, t => ...)` animates anything. Each takes an optional curve from `Ease` (`Linear`, `InQuad`, `OutQuad` (the default), `InOutQuad`, `InCubic`, `OutCubic`, `InOutCubic`, `InOutSine`, `InBack`, `OutBack`, `OutElastic`, `OutBounce`) and returns a tween you can `Cancel()` or chain with `.Then(...)`. A new `MoveTo` takes over from an unfinished one. Like timers, tweens run on game time and stop if their entity is destroyed; `Game.MoveTo(entity, ...)` and friends work on any entity.
- **Systems:** a `GameSystem` does something every step for a whole kind of thing at once, rather than for one entity: override `Update`, and `Phase` to say when it runs (`Begin`, `Logic`, `Scripts`, `Simulate`, `Physics`, `Resolve`, `Present` or `End`; `Logic` unless it says), after Griff's own steps there. `AddSystem(new Magnets())` in a script's `Start` runs it from that frame until the entity goes (destroyed, left behind by a scene change, or the script reloaded); `Game.AddSystem` runs one for the rest of the game, and its `SceneChanged` is called when the scene changes, to forget what it kept. A game has one system of each type, so adding another replaces it (and a scene that adds one each time it starts still runs just one). `Game.RemoveSystem`, `Game.GetSystem<T>()` and `Game.Systems` find and stop them, and `Game.Frame` lists every step of a frame in order, Griff's and yours. Systems run while the game is paused too (`Time.Delta` is 0 then). Override `Draw(FrameDrawing frame)` to draw what the system knows of that no one entity draws, once every entity has drawn itself: `frame.AddOverWorld(...)` puts quads (`FrameDrawing.Quad`, `Line`, `Label`, `Outline`, `Circle`) over the world and under the HUD, and `frame.Sprites` can be changed in place (Griff's strategy, tactics, block world and tool overlays draw this way). A system opts into more by implementing a small interface, which the game finds once when the system is added: `IViewDrawer` draws the world its own way in `DrawView`, before any system's `Draw` (as the first-person view does); `ISeesThroughEyes` says whose eyes the game is seen through (`Eyes`, which `Game.Eyes` gives and sounds are heard from, and `LooksThrough`); `IHudLayout` lays a HUD out in `LayOutHud`, early each frame, and gives its buttons as `HudButtons` (all of them are `Game.HudButtons`); `INoticeSource` gives a short message as `Notice` (`Game.Notice`); `IMouseOnHud` says when the mouse is over what the system draws (`MouseOnHud`), so clicks there aren't meant for the world; `IPlayerTools` runs one player's own controls (a hotbar, building tools) for each other player on the host of a network game, handed the player and their controls (`RunForPlayer(player, input)`: `Game.Input` stays this machine's, `Game.Owned<T>(player)` is what they have and `Game.HudViewOf(input)` the view they see, so what the system keeps for each player stays theirs); `IClaimsBack` says there is something under way that Back (Esc) cancels (`ClaimsBack`), so a pause menu waits (a script overrides its own `ClaimsBack` instead); `IPlayersListener` hears `PlayersChanged` in a network game as each scene starts and whenever the players change; `IGridStepListener` hears of each grid step before it's taken (`GridStepStarting`: a move history remembers where everything was); `IPlayerSettings` keeps settings a player chooses for themselves with the game's own (read back from the save data as the game starts, written as they change, and shown and set by a Slider or Toggle set to one of them: the Rhythm module's note speed and the Visual Novels module's skip and auto speed are kept so); `IReadingPreferences` says how the player likes dialogue skipped and read on, and which lines they've read; and `ISceneEndListener` hears `SceneEnding` as the game leaves a scene or stops, to keep what it leaves behind (a block world saved). The same `Game` members Griff's own genres use (`Game.PlaySoundAt`, `Game.Warn`, `Game.TurnOrderOf`, `Game.Owned` and the rest) are public and documented, so a system of yours can do whatever a genre's does, and the frame's steps are named by `Steps` members (`Steps.Dragging`), so `Game.Frame` and a module's `StepAfter(Steps.Dragging, "Match Boards", ...)` agree on where a step goes. A system's fields are its state: a rewind takes them back with the rest of the game (see [Rewind](#rewind)), lists, dictionaries and the entities they point to included; mark one that isn't with `[NotState("why")]` (a cache, a list kept only to reuse) and override `Restored` to work it out again once a rewind has put the rest back.
- **Joining in with Griff's components:** a script component can be what a **Counter** counts (a property marked `[Output]`; the counter's Track and Output name it, `*` for the first in the scene), steer a **Grid Movement** itself instead of the player's keys (`IGridDriver`: its next step, and whether each step uses a turn's action), get ready as each of its side's turns begins (`ITurnReady`), say when an action can't be done now so a button doing it fades (`IActionReady`), name what it does as actions (`[Action]` on a method, taking nothing or one number, whole number, true-or-false or name: a **Trigger** or a wire can then do it, and `Game.Do(entity, "Explode")` does it from code; one that needs part of the game can first take the service it needs, as Griff's own components do: `[Action] void Pause(IPanels panels)`, with `IFlags`, `ISpawning`, `IDialogues`, `IMinigames` and the rest, each provided by the system that owns that part, and a system a script adds can provide one of its own, an interface of `IGameService`; `Game.Services.Get<IFlags>()` asks for one from code), say what a switch turns on and off (`[Powered]` on a true-or-false property, such as its own `Running`), name what it knows as outputs (`[Output]` on a property giving a number, whole number, true or false, or a name, with `OutOf` naming what it's out of, or on a method taking a name, read as `"Cargo:Ore"`: a **Counter**, **Bar**, **Text** or wire can then show it, once the script has started, so its getter can use `Game` (one spawned this step is read from the next), and `ComponentOutputs.Read(entity, "Lives")` reads it from code), take the player's input only while it has focus (`[Focus]` on its true-or-false `Controllable`, which gives it a Focus action, or `[FocusFrom(typeof(PlatformerMovement))]` on the class to go by another's; see [Mixing genres](/docs/mashups/)), do something when a message is sent (`IHearsMessages`, as a dungeon goes down a floor on its Next Floor Message), keep keys and the mouse wheel for itself (`[ClaimsKeys(Key.D1, ..., Wheel = true)]`, as a hotbar does, so building tools are picked by clicking), and tell an agent playing the game what it's up to (`IPlaytestReport`), the same way Griff's match boards, tactics units, goals and dungeons do. Each genre's types (`MatchBoard`, `Commander`, `TacticsUnit`, `Weapon`, `MoveHistory`) live in their module's namespace (`Griff.Modules.Strategy`, `Griff.Modules.FirstPerson`), which scripts get without a `using` once their project uses the genre (another is a `using` away), and what a genre offers on `Game` (`Game.Train`, `Game.Craft`, `Game.Equip`, `Game.Undo()`) comes with it.
- **Drawing:** a component that implements `IDrawable` draws itself, as Griff's sprites, tilemaps, text, bars, particles, boards, ropes and wires do: `Draw(Drawing drawing)` adds quads (`Rect` in the entity's own space; `Quad`, `Line`, `Outline`, `Circle`, `Text`, `Image` and `Bar` in the world), knowing what's in view (`View`), how thick a thin line is (`LineWidth`) and whether the game is running (`IsPlaying`). The game is drawn between its last two steps: transforms get there by themselves, and what a component draws from its own numbers gets there through `drawing.Blend`, a `StepBlend` saying how far the frame is between the steps. Keep a number, point or color that changes in a `Smoothed<T>` field (`_angle.Set(this, value)` as the game runs), and draw `drawing.Blend.Of(_angle)` (`TurnOf` for an angle, the short way round), or keep what it was yourself and draw `drawing.Blend.Between(was, now)`; it follows the transforms' rules, drawing as it is whatever jumped. A system drawing the game's own state asks `frame.Blend` (or `frame.BlendOf(entity)`), and draws a HUD it laid out over the camera's view during the step through `frame.PinToView`, so it keeps still on the screen as the camera moves, zooms and shakes between steps; an `IDrawEffect` asks `StepBlend.For(entity)`. `DrawOrder` places the entity among the others (0 unless it says; an entity's sprite sets it if it has one) and `DrawStep` places its draws among the entity's own (`DrawSteps.Shapes`, over the sprite, unless it says; from `DrawSteps.OverWorld` on, after the whole world). Each frame, `Draw` is called once for each entity in hierarchy order (the Hierarchy panel's, top to bottom), and for an entity's drawables in `DrawStep` order; the draws are then put back to front, each entity's kept together, so a drawable is called in the same order whatever layer it's on and doesn't see what's drawn under it (a system's `Draw` sees the whole frame). `IDrawEffect` changes how its entity is drawn without drawing (a tint for its sprite and text, a transform for it and what's inside it, or drawing it over everything), and `ILightSource` gives off light in the dark. The Scene view draws them too.
- **Networking:** see [Multiplayer](/docs/multiplayer/#multiplayer) for `[NetLocal]`, `[PlayerMovement]`, `[Sent]`, `[NotSent]` and `INetState`.
- **Game:** `Spawn` (an entity, or a prefab by name), `Destroy`, `Find` and `FindAll` (by name, or `Find<T>()` and `FindAll<T>()` by component type; `Game.Scene.Active<T>()` gives just those on enabled entities, `Game.Scene.Running<T>()` those of them that aren't paused, and asking every frame costs next to nothing, since the scene keeps its components by type), `LoadScene` and `ReloadScene` (optionally with a `fade:` in seconds and a `color:`; fades use real time, so they work from a paused menu, and `IsChangingScene` is true until the switch), `KeepBetweenScenes` (carry an entity into the next scenes, still running, as a **Keep Between Scenes** does: it takes the place of one of the same name there, and returning to its original scene doesn't duplicate it), `LoadScene(name, fade, color, arriveAt:)` and `ArrivedAt` (the players arriving at a named spot; see [Doors between scenes](/docs/mashups/#doors-between-scenes)), `PlayMinigame`, `WinMinigame`, `LoseMinigame` and `Minigame` (another scene played on top; see [Minigames](/docs/mashups/#minigames)), `Remembers`, `RememberedScenes`, `ForgetScene`, `ForgetScenes` and `NewGame` (scenes that remember how they were left; see [Scenes that remember](/docs/mashups/#scenes-that-remember)), `Quit`, `ShakeCamera`, `HitStop` (freeze game time for a moment of real time; `Time.IsFrozen` is true meanwhile), `SlowMotion` (game time at a fraction of normal speed for a moment of real time, easing back; on top of `Time.Scale`, and `Time.SlowMotionScale` says how much), `Flash` (an entity's sprites and its children's), `FlashScreen` (the whole view, fading out on real time), `Fullscreen` (remembered, like the volumes), `Focus` and `Focused` (the button or slider picked for the keyboard and gamepad), `OpenPanel`, `ClosePanel`, `TopPanel`, `OpenPanels` and `IsPaused` (see [Pause menus and panels](/docs/ui/#pause-menus-and-panels)), `Pause` and `Resume` (a group of the game: see [Pausing part of the game](/docs/ui/#pausing-part-of-the-game)), `ListenForBinding` and `Rebinding` (see [Controls screens](/docs/ui/#controls-screens)), `FadeThrough` (fade out, call something while the screen is covered, fade back in), `LightAt` (how lit a place or an entity is, 0 to 1), `PlaySoundAt` (a sound from somewhere on or off screen), `StartDialogue`, `AdvanceDialogue`, `ChooseDialogue` (pick one of its choices) and `Talking` (the dialogue being said), `HasFlag`, `SetFlag` and `FlagChanged` (see [Dialogue flags](/docs/story/#dialogue-flags)), `SaveGame`, `LoadGame`, `DeleteSave`, `QuickSave`, `QuickLoad`, `HasSave`, `SavedGames` and `SavedGameIn` (see [Saving and loading](/docs/visual-novels/#saving-and-loading)), `Skipping`, `AutoReading`, `SkipUnread`, `AutoSpeed` and `HasRead` (see [Skip and Auto](/docs/visual-novels/#skip-and-auto)), `SaveData` (`GetInt`, `GetFloat`, `GetBool`, `GetString`, `Set`, `Delete`, `Clear`) and `Physics`, the physics world of the scene being played (`Raycast`, `OverlapPoint`, `OverlapBox`, `OverlapCircle`, each about that scene and taking `layers:` to only find colliders on those layers; `Impacts`, the last step's hard knocks, and `Pushes`, what shoved what along, each pair once, the pusher first: `foreach (var (pusher, crate) in Game.Physics.Pushes)`).

Built-in components:
- `Transform`, `Camera` (with `Shake`), `Sprite` (single images or sprite sheets, optionally nine-sliced with **Slice**) and `Text` (with `Align` and a `Font`: a `.ttf`/`.otf` file in Assets, which ships with the game, Griff's free fonts among them once picked, or an installed font's name; see [Text and fonts](/docs/text-and-fonts/#text-and-fonts));
- `Animation`, which plays a range of sprite-sheet frames, or named **Clips** (`Idle 0-3, Run 4-9 @12, Die 11-15 once`: a name, frames, and optionally a speed and `once`; frames written high to low, `Close 3-0`, play backward) switched with `Play("Run")`, which carries on if that clip is already playing. Its **Clip** picks the one the game starts with. `ClipSettings("Run")` gives the frames, speed and looping a clip plays with, `Animation.FrameAt(time, count, fps, loop)` which of its frames shows at a time, and `Animation.FormatClips` writes clips back as text. Its **Time** (seconds into the clip) is kept with the scene, so a rewind comes back to the frame it was on;
- `Tilemap`, a painted grid of tiles from one or more tilesets, square, isometric or hex (`Shape`, `TileHeight`, `SortByY`, `Neighbors(x, y)`; see [Isometric maps](/docs/tilemaps/#isometric-maps) and Hex maps above) (`GetTile` and `SetTile` from scripts, `Tilesets()` and `TilesetOf(tile)` to see which picture a tile is cut from, `FillRect(fromX, fromY, toX, toY, tile)` for a box and `FloodFill(x, y, tile)` for an area, like the Scene view's tools; -1 clears; each takes a `TileTurn` after the tile to lay it turned or flipped, and `GetTurn(x, y)` and `SetTurn(x, y, turn)` read and change a cell's turn, see [Turned and flipped tiles](/docs/tilemaps/#turned-and-flipped-tiles); `Layers()` lists the map's layers, `LayerOf` the map a layer belongs to and `BaseMap` the one whose tilesets it uses), with background and one-way tiles. `FindPath(from, to)` finds the shortest way between two world positions around its solid tiles (background and one-way tiles can be walked through), as waypoints at the corners, moving diagonally but never cutting a wall's corner (pass `diagonal: false` for four directions); `FindSteps(start, goals, avoid, blockingLayers)` plans in cells as a grid mover walks, every cell to the nearest of several goal cells round solid tiles, the cells to avoid (where others stand) and solid colliders on the layers named (null if none can be reached; `diagonal: true` for eight ways);
- `ParticleEmitter`, for continuous effects or one-shot bursts (`Burst`; `BurstOnStart` with `DestroyWhenFinished` makes a self-cleaning effect prefab);
- `Button`, a clickable area that lightens under the mouse and darkens while held, with a no-code **Action** (and **Fade**), a **Shortcut** key or a **Shortcut Action** (an input action, so the player can rebind it: `button.ShortcutAction = "OpenMap"`), `OnClick` for behaviors on its entity and a `Clicked` event for anyone else, **Over Panels** to keep working while a panel it isn't on is open (a menu bar over a letter), and `IsFocused` while the keyboard or gamepad has picked it. Put the label `Text` on the button's own entity so it's shaded with it;
- `Slider` (`Value` between `Min` and `Max`, `Snap`, a `Changed` event) and `Toggle` (`IsOn`, a `Changed` event; it needs a `Button` on its entity), each with a **Setting** or **Save As**; see [Options](/docs/ui/#options-menus);
- `Panel` (**Open**, **Pause Game**, **Pauses**, **Open On**, **Open Message**, **Close On Back**, **Popup**, **First**, `IsTop`, and `Opened` and `Closed` events) and `Choice` (`Index`, `Value`, `Step`, a `Changed` event; it needs a `Button` on its entity); see [Pause menus and panels](/docs/ui/#pause-menus-and-panels), and Controls screens above. Buttons have `Interactable`, `NeedsSaved`, `IsUsable` and `IsListening`, and `Text` has `Shown`, its value with button prompts filled in;
- `Bar` (`Value`, `Max`, `Fraction` and **Track**); see [HUD bars](/docs/ui/#hud-bars);
- `Chart` (`Add(value)`, `Add("Cards", value)`, `Set("Cards", values)`, `Clear()`, `Clear("Cards")`, `Points("Cards")`, `SeriesNames` and `Bounds`); see [Charts](/docs/ui/#charts);
- `Pickup` (`IsCollected` and a `Collected` event) and `Counter` (`Value`, `Best`, `Add` and a `Changed` event); see [Pickups and scores](/docs/ui/#pickups-and-scores);
- `Follow`, which moves its entity after a **Target**, smoothly, with an offset, dead zone, look-ahead and an area to **Stay Inside** (see [Cameras](/docs/players/#cameras)). Scripts can set `TargetEntity` to follow something directly. `Camera` has **Sort By Y** for top-down games;
- `SortGroup`, which sorts its entity and everything inside it as one at the root's feet (plus a `GroundOffset`), its parts by their own Order within it, for a thing made of several sprites in a top-down game;
- `Parallax`, which scrolls a background layer at a fraction of the world's speed and can repeat its sprite across the view;
- `ScreenAnchor`, which pins its entity to one of nine points on the screen plus an **Offset**, for HUDs and menus. The Scene view shows it where the game does (on a 16:9 screen) and dragging it there sets its Offset; with the camera's **HudSize** set, it keeps its size on the screen as the camera zooms (`Camera.HudScale` says how many times its own size it's drawn);
- `Health` (`Damage`, `Heal`, `Kill`, `IsDead`, `IsInvincible`, **Respawn**, and `Damaged`, `Died` and `Respawned` events) and `DamageOnTouch` (with **Kill**); see [Health and hazards](/docs/combat/#health-and-hazards) and Checkpoints above. `Sprite` has `Flash` and `Blink` for scripts to use themselves;
- `Spawner` (with an `EntitySpawned` event, `Spawned` and `Alive`) and `Lifetime`;
- `Typewriter` (`IsDone`, `Skip`, `Restart` and a `Finished` event);
- `Mover` (`Path`, `OffsetAt`, `TripSeconds`, `IsFinished`), `Patrol` (`Turn` and a `Turned` event), `Chase` (`IsChasing`, `TargetEntity` and a `Spotted` event) and `Shooter` (a `Fired` event, to give each shot something extra); see [Moving platforms](/docs/combat/#moving-platforms), Enemies and Turrets above;
- `Draggable` (`IsHeld`, the `Target` it's on, and `PickedUp` and `Dropped` events) and `DropTarget` (`Contents`, `IsFull`, `AcceptsKind`, and `Received` and `Taken` events), with `Game.Held` for what the mouse holds, and `Grabber` (`Holding`, `InReach`, `Press()`, and `PickedUp` and `Dropped` events); see [Drag and drop](/docs/story/#drag-and-drop);
- `WallRooms` (`Rooms`, `Room(id)`, `RoomAt(cell)` or `RoomAt(point)`, `RoomOf(entity)`, `EntitiesIn(room)` and `EntitiesIn<T>(room)`, `Refresh()` and a `Changed` event; each `WallRoom` has `Id`, `Cells`, `Area`, `Open` and `Closed`), the rooms a map's walls make (see [Rooms from walls](/docs/tilemaps/#rooms-from-walls));
- `SelectionControls` (`Selected`, `SelectedCount`, `Select(entity)` or `Select(entities)`, `Add`, `Remove`, `Clear()`, `Group(n)` and `SetGroup(n, entities)`, `Busy` to leave the mouse to a tool in hand, `IsDragging`, and `SelectionChanged` and `Commanded(target, point, selected)` events, a right-click's target, the point in the world and what's selected) and `Selectable` (`IsSelected`, `KindName`, `Area()`, and `Selected` and `Deselected` events); its keys are the input actions `Select`, `Command`, `AddToSelection`, `AssignGroup` and `Group1` to `Group9` (`InputActions.GroupActions`). A selection of your own rules keeps a `SelectionState` and answers its `ISelectionRules`, as the strategy Commander does; see [Selecting as in a strategy game](/docs/players/#selecting-as-in-a-strategy-game);
- `Switch` (`IsOn`, which scripts can set, and a `Changed` event), `Inventory` (`Count`, `Has`, `Add`, `Take`, `AddItems`, `TakeItems`, `Clear`, `Use`, `Usable`, `ForTeam`, and `Changed` and `Used` events) and `Trail` (`Copies`); see [Switches](/docs/story/#switches), Keys and inventory, and Trails above. The ideas every genre shares: `Teams.Of(entity)`, `Teams.Allies(a, b)`, `Teams.Foes(a, b)` and `Game.TeamOf(entity)` for teams (a component of your own joins one by implementing `ITeamMember`), and `Game.CountItems(entity, item)`, `Game.CanPay(entity, cost)`, `Game.Pay(entity, cost)`, `Game.Give(entity, item, count)` and `Items.Parse(text)` for items (one of your own holds them by implementing `IHoldsItems`); see [Mixing genres](/docs/mashups/#teams). Movers, spawners and shooters have `Running` and doors have `Locked`, which switches set and scripts can too. A tilemap's `ShownTile` is the frame an animated tile is showing;
- `Checkpoint` (`Active` and a `Reached` event), `Door` (a `Used` event) and `Dialogue` (`IsTalking`, `Line`, `Said` and `Speaker` (the line showing and who says it), `IsDone`, `Choices` and `Picked`, and `LineShown`, `Chosen` and `Finished` events); see [Checkpoints](/docs/combat/#checkpoints), Doors and Dialogue above. `Follow` has `Snap`, to jump straight to its target after a teleport of your own;
- `PlatformerMovement` (`Facing`, `IsClimbing`, `IsDashing`, `IsWallSliding`, `JumpsLeft`, `JumpSpeedFor(gravity)`, `Launch`, and `Jumped`, `Landed` and `Dashed` events) `TopDownMovement` (`Facing`, `IsDashing`, **Isometric**, and a `Dashed` event) and `GridMovement` (`Facing`, `IsMoving`, `Cell`, `Step(x, y)` to step from a script, even when it isn't **Controllable**, `FindMap()` for the tilemap it steps on, `CellAt(world)` and `CellCenter(cell)`, `WalkTo(cells)`, `WalkTo(cell)` or `WalkTo(entity)` to walk somewhere and `StopWalking()`, with `IsWalking`, `IsWaiting`, `Goal` and `Destination`, and `Stepped`, `Bumped`, `Arrived` and `Blocked` events; see [Walking somewhere on a grid](/docs/players/#walking-somewhere-on-a-grid)); see [Players with no code](/docs/players/#players-with-no-code). `GridChase` has `IsChasing`, `TargetEntity` and `Spotted` and `Reached` events, and `GridPatrol` has `Leg`, `Away`, `Path`, `PointOf(leg)`, `IsFinished` and an `Arrived` event (see [Grid enemies](/docs/combat/#grid-enemies)). `Animation` has `HasClip`;
- `Ladder`, `Pushable` (`OnGoal`, and a `Pushed` event on a grid; off a grid, `Game.Physics.Pushes` says what shoved it), `Goal` (`CoveredBy`, `IsCovered`, `AcceptsKind`, and `Covered` and `Uncovered` events), `MoveHistory` (`Undo()`, `Restart()`, `Record()`, `Moves`, `CanUndo`, and `Undone` and `Restarted` events), `Bouncer` (a `Bounced` event) and `Crumble` (`Crumbled`, `IsShaking`, and `Fell` and `Returned` events); see [Ladders, crates, springs and crumbling ledges](/docs/players/#ladders-crates-springs-and-crumbling-ledges). A tilemap's `IsLadder` and `LadderAt` find its ladder tiles, and `ShownTile(x, y, tile)` is the piece an autotile shows;
- `Light` (`CurrentIntensity`, its flicker included, `WorldCenter` and **Shadows**), `Weather` and `ScreenEffects` (see [Shadows](/docs/atmosphere/#shadows), Weather and Screen effects above), `Room` (`Area`, `IsCurrent` and an `Entered` event), `SoundSource` (`Loudness`), `BeatClock` (`IsPlaying`, `Beat`, `Bar`, `BeatInBar`, `Beats`, `Phase`, `Position`, `CurrentTempo`, `SecondsPerBeat`, `SecondsFromBeat`, `OnBeat(every, offset)`, and `BeatStarted` and `BarStarted` events) and `BeatPulse` (`Scale`); the camera has **Ambient**, `CurrentAmbient` (a room's, as it fades in) and `IsDark`, and **Transition**, and `Follow` has `CurrentRoom` and `IsChangingRoom`. See [Rooms](/docs/atmosphere/#rooms), Lights and darkness, Scene transitions and Sounds in the level above;
- `Unit` (`MoveTo`, `AttackMoveTo`, `Attack`, `Stop`, `Hold`, `Order`, `Destination`, `Target`, `IsSelected` and an `Arrived` event), `Worker` (`Gather`, `Carrying`, `Load`, `Source`), `Building` (`IsBuilt`, `Rally`, `IsSelected` and a `Finished` event), `Producer` (`Queue`, `Progress`, `Options` and a `Trained` event), `Fighter` (`Target` and a `Swung` event) and `Faction` (`Amount`, `Add`, `CanAfford`, `Spend`, `Refund`, `Lacking`, `IsDefeated` and a `Defeated` event); `Costs.Parse` reads a cost. `Game.Selected` and `Game.Select` are the player's selection, `Game.Train`, `Game.CancelTraining` and `Game.PlaceBuilding` do what the command card does (saying why not when they can't), `Game.FactionOf`, `Game.SupplyUsed` and `Game.SupplyProvided` look up a team, `Game.IsVisible` asks the fog of war, and `Game.LookAt` points the camera; see [Real-time strategy](/docs/genres/#real-time-strategy);
- `Multiplayer`, `Networked`, `PlayerSpawn`, `NameTag`, `Scoreboard` and `TextField` (`Value`, `IsFocused`); see [Multiplayer](/docs/multiplayer/#multiplayer). `Scene.FindNamed` finds an entity by name, this machine's player's own first;
- `Walker` (`Feet`, `IsWalking`, `Route`, `Said`, `Hovered`, `HoveredHotspot`, `HoveredItem`, `WalkTo`, `Stop`, `Say`, `ChooseLook`, `ChooseUse`, and `Arrived`, `Spoke`, `Did` and `UsedItem` events), `Hotspot` (`StandPoint`, `Called`, `CanUse`, `Look()`, `Use()`, `UseItem(item)` and a `Done` event), `InventoryBar` (`Holding`, `Pick`, `PutBack`, `Combine("Rope + Hook")`, `LookAt`, `DescriptionOf`, `IconOf`, `CombinationOf`, and `Picked`, `Combined` and `Looked` events), `UseWith` (`Takes(item)` and a `Done` event) and `WalkArea` (`Contains`, `ScaleAt`, and `WalkArea.ScaleIn(scene, point)`); `Input.Cursor` sets the pointer over the game (`MouseCursor.Hand`, `Cross`, `Look`, `Hidden` or `Arrow`, on the desktop and in a browser); see [Point-and-click adventures](/docs/adventures/). A script component whose shape is a ring of points (a string `Points` property, "x y; x y") can be an `IPointOutline`, and the Scene view edits its points as it does a polygon collider's;
- `FirstPersonMovement` (`Facing`, `Pitch` and `Stride`) and `Weapon` (`IsCoolingDown`, and `Fired` and `Hit` events); `Game.FirstPersonView`, `Game.FirstPersonViewer`, `Game.CurrentWeapon`, `Game.Equip` and `Game.Fire` (a shot without ammo, for cutscenes), and `Input.MouseDelta`, `Input.WantsMouseLock` and `Input.IsMouseLocked` for mouse look of your own; see [First-person shooters](/docs/genres/#first-person-shooters);
- `Attack` (`Swing`, `IsAttacking`, `IsHitting`, and `Swung` and `Hit` events); see [Swords and close-up attacks](/docs/combat/#swords-and-close-up-attacks). `TurnOrder` (`EndTurn()`, `IsTurn(team)`, `IsComputer(team)`, `IsBetweenTurns`, `TeamList`, and `TurnStarted` and `TurnEnded` events; set `Current` to hand the turn to a team) and `TurnTaker` (`IsTurn`, `Act(count)`, `ActionsLeft`, and `TurnStarted` and `TurnEnded` events); see [Turns](/docs/turns/#turns). Tilemaps have `SlopeOf`, `SlopeCells` and `Physics.SlopeShapes` (and an isometric or hex map's walls, `Physics.WallShapes`);
- `BoxCollider`, `CircleCollider`, `PolygonCollider` and `Body`, for physics. Boxes, circles and polygons collide in any combination. `GetComponent<Collider>()` finds any kind. A polygon's `Points` are its corners ("-1 0; 1 0; 0 1", in its own units; `PolygonCollider.FormatPoints(points)` writes a list of them so), `WorldPoints` its corners in the world as it is, `WorldCentroid` where it balances (what it turns round), `ToWorld(point)` and `FromWorld(point)` turn a point of its own into the world and back, `PieceCount` and `PieceCorners(piece)` are the convex pieces a concave one is split into, and `Problem` says what was wrong with points that don't make a proper shape and how they were fixed up (null when they do; see [Polygon colliders](/docs/players/#polygon-colliders)). A body's `Bounciness` (0 to 1) makes it bounce off what it hits, settling once the bounces get small. With `Rotates` on it turns: `Spin` is how fast (degrees a second, anticlockwise), `PushAt(force, point, seconds)` pushes it at a point in the world (off its middle, it turns too), `Twist(torque, seconds)` turns it (by the torque over its `Inertia`, from its `Mass` and collider's size, round its `CenterOfMass`), and `Grip` (0 to 1) is how well it grips what it slides along. A ball (a `CircleCollider` on a body that rotates) rolls, slowed by its `RollingResistance` (0.1 by default; 0 rolls on and on): kick one by setting its `Velocity` (`ball.Velocity = new Vector2(8, 3);`, and it rolls on when it lands) or with `PushAt` under or over its middle for backspin or topspin (see [Balls that roll](/docs/players/#balls-that-roll)). `Asleep` is true while it rests asleep with what it touches (set it, or its `Velocity` or `Spin`, to wake it), and `StillFor` is how long it's been still. A collider's `Turns` says whether it turns with its body; a box's `WorldShape` (middle, half size and turned axis) and `WorldCorners` are the box as it's turned (`WorldBox` is the box round it), a polygon's `WorldPoints` are its corners as it's turned, and any collider's `Contains(point)` checks a point against it as it is (`WorldBounds` is the box round any kind). `Ground` is what it's standing on; when that moves (a platform moved by a script or a tween) the body rides along with it. `WasGrounded(0.1f)` is true while grounded and for that long after walking off an edge (but not after jumping), `AirTime` is how long it's been off the ground, and `WallSide` is -1 or 1 while a wall on that side stops it (for wall jumps), and `DropThrough()` falls through one-way platforms for a moment (`IsDroppingThrough`; `Game.Physics.OnlyOneWayBelow` says whether there's anything else underneath). Give colliders a **Layer** (`Player`, `Enemy`, `Bullet`; empty is `Default`) and list what they pass through in **Ignore Layers**, e.g. `Player, Bullet` on a bullet: ignored pairs don't block each other, collide or trigger, and only one side has to list the other. Tilemaps have both too.

Saving a script while the game is playing starts the game over with the new version, as a fresh Play would (with as many players, in every player's window when playing with several), from the scene as it was when you pressed Play. The scripts aren't swapped into the game as it stands: each would start again (spawning what its `Start` spawns a second time) without the fields of its own it had, and the others would read scripts that hadn't started yet.

## Rewind

Rewinding puts the whole game back as it was, and the same game goes on from there just as it did, to the last bit: the scene as its file would hold it, and everything the game kept besides. That's every system's state (timers, coroutines and tweens with where they'd got to, message listeners, systems scripts added, the dialogue seen and a conversation under way, a swing under way, a respawn countdown, which control is picked, the camera's slide into a room, a strategy game's selection, orders, training queues and fog), the clock (slow motion, a hit stop), the game's random numbers (so the same rolls come up again), the save data (a best score saved since goes back too), which scripts have started (a script that had doesn't start again), and each component's own fields that the scene file doesn't hold: a script's private fields, a body's time in the air, a sprite's flash or blink, live particles, a falling-block board's piece and those to come, a match-3 board's slides and falls. A coroutine, or a lambda given to `After`, `Every` or `Tween`, goes back with what it holds: where it had got to, its variables, and the entities and components it points to (the ones put back). What can't be rewound is kept as it is: an object of .NET's or a library's other than its collections (a `StringBuilder`, a `Stopwatch`, a `Task` and so an `async` method, a file, a socket, a `Random` of your own: use `Rand`, or a `GameRandom`, which is a `Random` that rewinds), anything `static`, and a field marked `[NotState("why")]`, which you can mark yourself to leave something out (a cache, a list kept only to reuse; one of a script's starts as a new script's would). Sound isn't rewound: sounds playing stop and sound sources start theirs again, and music plays on (a beat clock and a beat pulse follow it). Rewinding works with one player, not in a network game or Play with Players. Movers, patrols, checkpoints, panels and pause groups keep their place in the scene file too (a mover's **Time**, a patrol's **Walked**, a checkpoint's **Active**, a panel's **Open**, a pause group's **Paused**), so rewinding into a pause keeps the pause menu up, and so do puzzles: switches, what they've switched (**Running**, **Locked**), keys used up, inventories and a match-3 board's gems and **Swaps**. A ledge that has crumbled stays gone (its **Crumbled** is saved) and comes back in its own time.

## Rewind in big scenes

A rewind snapshot holds what the scene file would and loads back as the file would (the same ids; a color keeps its 8 bits a channel, and a number that isn't finite comes back as the property's default), kept in a compact binary form made without garbage. With it goes what the game keeps besides (see above), written as the snapshot goes through the entities: the systems' fields, and each component's own unless it's at rest (a sprite that isn't flashing or blinking has nothing to keep, nor has a transform), so BunnyMark's 50,000 bunnies take as long to copy out as before (3.07 ms against 3.03 in a Release build) and make about 5 KB of garbage a snapshot. Taking one is split so the game waits for as little of it as it can. Between two steps, each entity's values are copied out into a record of its own, its strings kept as they are (a string never changes) and lists of lines copied, so the snapshot is that moment whatever happens after; the code that copies a component is compiled for its type the first time it's seen, so this is little more than reading the values, even in a Debug editor. Then, on another thread (it reads nothing but the copies), each record is compared with the entity's record in the last snapshot and only the bytes that changed are kept: about 25 bytes for a bunny that moved, 4 for a wall that didn't. Every tenth snapshot is kept whole, so rewinding to one decodes at most ten, and one that's dropped stays in memory (and counted) until nothing kept is written against it. Snapshots are taken ten times a second. A big scene's are taken less often only so that they fit in 256 MB (`Timeline.MemoryBudget`) while still reaching back about 20 seconds, or so that copying them out costs a step half a millisecond at most on average in the editor (`Timeline.CostPerStep`, at most two seconds apart): a step waits for it, and a slow one makes that frame late. A BunnyMark bunny takes about 35 bytes a snapshot (200 before). 20,000 of them take 1.1 ms to copy out (2.8 ms in a Debug editor) and 1.5 ms (7.2 ms) to settle on the other thread, and they're kept ten times a second in both (before: 3.5 ms (15 ms) all on one step, every 0.6 seconds (every two)); 50,000 take 2.9 ms (6.6 ms), kept about seven times a second (four). Rewinding a script rebuilt while the game ran reads its values by name, as the scene file would. While the game plays, the Hierarchy and the Inspector catch up with it ten times a second (`SceneDocument.RuntimeRefreshed`) rather than every frame, and as it pauses or steps; the Hierarchy keeps the rows of entities still there and makes only the rows in sight, so a scene of 20,000 entities fills it at once.
