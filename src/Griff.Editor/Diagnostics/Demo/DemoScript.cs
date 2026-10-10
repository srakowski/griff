#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Assets;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.Projects;
using Griff.Editor.Features.SceneView;
using Griff.Editor.Themes.Controls;
using Griff.Scenes;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using Slider = System.Windows.Controls.Slider;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// The guided demo: builds "Ember Hollow", a small platformer, from an empty project, narrating each step and
/// driving the editor with real input.
/// </summary>
internal sealed class DemoScript(DemoUi ui, IWorkspace workspace, ISelection selection, PlayMode play, SceneViewControl sceneView, DemoOptions options)
{
    private const string ProjectName = "Ember Hollow";

    private DemoInput Input => ui.Input;
    private DemoOverlay Overlay => ui.Overlay;

    public async Task Run()
    {
        ui.Log = Log;
        await Intro();
        await CreateProject();
        await Tour();
        await ArtPack();
        await PaintLevel();
        await AddPlayer();
        await FollowCamera();
        await FirstPlaytest();
        await Coins();
        await Enemies();
        await KeyScript();
        await LockedDoor();
        await Atmosphere();
        await Menus();
        await PlayThrough();
        await Ship();
        await Outro();
    }

    // ===== 0. Intro =====

    private async Task Intro()
    {
        Overlay.ShowCard("A guided tour of Griff", "Let's build a game", "From an empty project to a finished platformer with enemies, a locked door, lighting, a menu and a shipped .exe.");
        await Task.Delay(600);
        await ui.Say("Hi! In this walkthrough we're going to build a complete little platformer in Griff, a game engine made for game jams.");
        await ui.Say("We'll go from an empty project to a game with a painted level, enemies, a key and a locked door, a custom script, lighting, a menu, and a standalone exe you can share. I'll drive the editor. Just sit back and watch.");
        Overlay.HideCard();
        await Task.Delay(500);
    }

    private async Task Chapter(int number, string title, string subtitle)
    {
        Log($"chapter {number}: {title}");
        ui.Unspot();
        await Overlay.Card($"Part {number}", title, subtitle, options.Fast ? 150 : 1500);
    }

    // ===== 1. Project =====

    private async Task CreateProject()
    {
        await Chapter(1, "A new project", "A project is a folder: scenes, art, scripts and settings.");

        var start = (FrameworkElement)ui.Main.StartHost.Content;
        await ui.Explain(start, "The start page", "When Griff opens without a project, you land on the start page. Every game starts as a project, so let's make one. Control Shift N opens the New Project dialog.");
        await ui.FocusEditor();
        await ui.Shortcut(Key.N, ModifierKeys.Control | ModifierKeys.Shift);
        await WaitUntil(() => Application.Current.Windows.OfType<NewProjectWindow>().Any(w => w.IsVisible), 2500);
        if (!Application.Current.Windows.OfType<NewProjectWindow>().Any(w => w.IsVisible))
        {
            Log("retry New Project via the File menu");
            await ui.Menu("File", "New Project...");
        }

        var window = await DemoUi.WaitFor(() => Application.Current.Windows.OfType<NewProjectWindow>().FirstOrDefault(w => w.IsVisible), "the New Project dialog");
        await DemoUi.Settle(300);
        Overlay.BringToFront();

        var nameBox = (TextBox)window.FindName("NameBox");
        ui.Spot(nameBox, "Project name", dim: false);
        await ui.SayWhile("I'll call it Ember Hollow: a little forest with a door you need a key for.", async () =>
        {
            await Input.Click(DemoUi.Center(nameBox));
            await Input.Press(Key.A, ModifierKeys.Control, showBadge: false);
            await Input.Type(ProjectName, 75);
        });
        ui.Unspot();

        var list = DemoUi.Descendants<ListBox>(window).First();
        await ui.Explain(list, "Templates", "Griff comes with Platformer, Top-down and Arcade templates that are playable from the very first second. They're the fastest way to start a jam. Today, though, we'll start from Empty, so you can see how every piece fits together.");
        var empty = DemoUi.Descendants<ListBoxItem>(list).First(i => i.DataContext is NewProjectViewModel.TemplateOption { Template: Griff.Projects.ProjectTemplate.Empty });
        await ui.Click(empty);
        if (window.DataContext is NewProjectViewModel vm && vm.SelectedTemplate.Template != Griff.Projects.ProjectTemplate.Empty)
        {
            Log("FORCED template Empty");
            vm.SelectedTemplate = vm.Templates.First(t => t.Template == Griff.Projects.ProjectTemplate.Empty);
        }


        var create = DemoUi.Descendants<Button>(window).First(b => b.Content as string == "Create");
        await ui.Click(create);
        await DemoUi.WaitFor(() => workspace.Scene, "the new project's scene", 15000);
        await DemoUi.Settle(400);
        File.WriteAllText(Path.Combine(workspace.Project!.RootPath, ".griff-demo"), "Made by the Griff guided demo; safe to delete.");
    }

    // ===== 2. Tour =====

    private async Task Tour()
    {
        await Chapter(2, "Finding your way around", "Four panels and a toolbar: that's the whole editor.");
        await ui.Explain(ui.PanelContent("Hierarchy"), "Hierarchy", "On the left is the Hierarchy: everything in the scene, as a tree. A new scene has just a camera.");
        await ui.Explain(sceneView, "Scene view", "In the middle is the Scene view, where you lay out your level. You scroll to zoom, and right drag to pan.");

        await ui.Select("Main Camera");
        await ui.Explain(ui.Inspector, "Inspector", "On the right, the Inspector shows whatever is selected. In Griff an entity is just a name plus components. This camera is a Transform, which says where it is, and a Camera component.");
        await ui.Explain(ui.PanelContent("Assets"), "Assets", "Along the bottom are your Assets: the files in the project folder. Next to them is the Output panel, for logs and script errors.");
        var play = ui.ToolbarButton("Game.Play");
        var stop = ui.ToolbarButton("Game.Stop");
        await ui.Explain(DemoUi.Union(play, stop), "Play controls", "And up top are the play controls. F5 plays, F6 pauses and Shift F5 stops. We'll be using those a lot.");
    }

    // ===== 3. Art =====

    private async Task ArtPack()
    {
        await Chapter(3, "Bring in some art", "Anything you put in the project's Assets folder shows up straight away.");
        await ui.Say("Every game needs art. I've drawn a tiny pixel art pack for us: a tileset, a hero, a slime, coins, a key, a door and a torch.");
        DemoArt.WriteAll(Path.Combine(workspace.Project!.AssetsPath, "Sprites"));
        await DemoUi.Settle(800);
        await ExpandAsset("Sprites");
        await ui.Explain(ui.PanelContent("Assets"), "Sprites folder", "Copy files into the project's Assets folder and they appear in the Assets panel on their own, with no import step. Each image is a sprite sheet. The tileset, for example, is eight tiles across and three down.");
    }

    private async Task ExpandAsset(string folder)
    {
        var view = ui.PanelContent("Assets");
        var item = await DemoUi.WaitFor(() => DemoUi.Descendants<TreeViewItem>(view).FirstOrDefault(i => i.DataContext is AssetNode n && n.Name == folder), $"asset folder {folder}", 5000);
        await ui.Hover(item);
        var expander = DemoUi.Descendants<ToggleButton>(item).FirstOrDefault();
        if (expander is not null && expander.IsVisible)
            await ui.Click(expander);
        else
            item.IsExpanded = true;
        await DemoUi.Settle(200);
    }

    // ===== 4. Level =====

    private async Task PaintLevel()
    {
        await Chapter(4, "Paint a level", "Tilemaps: walls and floors you paint right in the Scene view.");

        await ui.Say("Levels in Griff are usually tilemaps. The Entity menu is where every kind of thing you can make lives. I'll create a tilemap.");
        await Deselect();
        await ui.Menu("Entity", "Create Tilemap", "Entity → Create Tilemap");
        await Rename("Level");
        await ui.SetNumber("Transform", "Position", 7, "Y", "Top-left corner of the map");

        await ui.Say("Now I point it at the tileset image, and tell it the sheet is eight tiles across and three down.");
        await ui.SetText("Tilemap", "Image", "Sprites/tiles.png", "Tileset image");
        await ui.SetNumber("Tilemap", "Columns", 8);
        await ui.SetNumber("Tilemap", "Rows", 3);

        await ui.Say("Next, I'll tell the map what its tiles mean. The first sixteen are autotiles, which pick the right edge piece for their neighbors by themselves.");
        await ui.SetText("Tilemap", "Auto Tiles", "0", "Tiles 0-15 fit themselves together");
        await ui.SetText("Tilemap", "One Way Tiles", "16", "Planks: jump up through, land on top");
        await ui.SetText("Tilemap", "Background Tiles", "18-23", "Grass, flowers, water: no collision");
        await ui.SetText("Tilemap", "Animated Tiles", "20-23 @6", "Water ripples through 4 frames");
        await ui.Say("Tile sixteen is a wooden plank you can jump up through. Grass, flowers and water are just background. And the water tiles ripple through four frames, six times a second.");

        // The level is text: paste it in.
        var row = await DemoUi.WaitFor(() => ui.InspectorRow("Tilemap", "Tiles"), "the Tiles field");
        var box = DemoUi.Descendants<TextBox>(row).First();
        ui.Spot(row, "A level is just text", dim: true);
        await ui.Say("Here's my favorite part. A tilemap is plain text, one character per tile. A dot is empty, zero is ground, g is a plank and k is water. I sketched this level ahead of time, so I'll just paste it in.");
        Clipboard.SetText(string.Join(Environment.NewLine, DemoLevel.Rows()));
        ui.Unspot();
        await Input.Click(DemoUi.Center(box));
        await Input.Press(Key.A, ModifierKeys.Control);
        await Input.Press(Key.V, ModifierKeys.Control);
        await Input.Press(Key.Tab, showBadge: false);
        await DemoUi.Settle(300);

        await ui.LookAt(0, DemoLevel.Top, DemoLevel.Width, DemoLevel.Top - DemoLevel.Height, 1400);
        var levelRect = ui.WorldRect(0, DemoLevel.Top, DemoLevel.Width, DemoLevel.Top - DemoLevel.Height);
        await ui.Explain(levelRect, "Ember Hollow, level 1", "Zooming out, there's our level: a pond to jump, a step, planks up to a high ledge, another pond, and the far wall where our door will go. Notice how the grass and the edges all lined up by themselves. Those are the autotiles at work.", dim: false);

        // Paint an island by hand.
        await ui.SayWhile("You can also paint directly. With the tilemap selected, I just drag in the Scene view. Let's add a little floating island above the start.", () => ui.Look(10, 2, 4.5f, 1200));
        foreach (var stroke in DemoLevel.IslandStrokes)
        {
            var points = stroke.Select(c => ui.World(DemoLevel.CellCenter(c.Column, c.Row).X, DemoLevel.CellCenter(c.Column, c.Row).Y)).ToList();
            await Input.Drag(points[0], points.Skip(1).ToList(), msPerSegment: 260);
            await Input.Pause(250);
        }

        await ui.Say("Each stroke is one undo step, and the edges keep fitting together as I paint.");
        await Input.Press(Key.Escape);
        await DemoUi.Settle();
        await ui.Say("Escape stops painting. And Control S saves. Scenes are small, readable JSON files, so they diff and merge cleanly in git.");
        await ui.FocusEditor();
        await ui.Shortcut(Key.S, ModifierKeys.Control);
        await ui.Look(9, -1, 5);
    }

    // ===== 5. Player =====

    private async Task AddPlayer()
    {
        await Chapter(5, "A hero who can run and jump", "A no-code platformer player, then make it look like ours.");
        await ui.Say("Now for our hero. Entity, Create Platformer Player makes a player that runs, jumps, and even wall-jumps, with no code at all.");
        await Deselect();
        await ui.Menu("Entity", "Create Platformer Player", "Entity → Create Platformer Player");
        await ui.SetVector("Transform", "Position", DemoLevel.PlayerStart.X, DemoLevel.PlayerStart.Y, "Start by the left wall");

        await ui.Say("It starts as an orange box. Let's give it the hero sprite sheet: eight frames in one row.");
        await ui.SetText("Sprite", "Image", "Sprites/hero.png", "The hero's sprite sheet");
        await ui.SetNumber("Sprite", "Columns", 8);
        await ui.SetVector("Sprite", "Size", 1, 1);
        await ui.SetText("Sprite", "Tint", "#FFFFFF", "White: show the art's own colors");

        await ui.Say("To animate it, I'll add an Animation component, and name the clips on the sheet: idle, run, jump and fall.");
        await ui.AddComponent("Animation");
        await ui.SetText("Animation", "Clips", "Idle 0-1 @3, Run 2-5 @12, Jump 6, Fall 7", "Name clips; the player plays them itself");

        await ui.Look(DemoLevel.PlayerStart.X + 3, -1.5f, 3.5f);
        var movement = await DemoUi.WaitFor(() => ui.ComponentHeader("PlatformerMovement"), "PlatformerMovement");
        movement.BringIntoView();
        await DemoUi.Settle(100);
        await ui.Explain(movement, "PlatformerMovement", "The Platformer Movement component is where you tune how it feels. Speed, acceleration, jump height, coyote time and jump buffering are all here, and all forgiving by default.");
        await ui.SetNumber("PlatformerMovement", "Air Jumps", 1, note: "1 = double jump");
        await ui.SetCheck("PlatformerMovement", "Dash", true, "Shift to dash");
        await ui.Say("One air jump gives us a double jump, which we'll need for those planks. I'll turn on dash too, just for fun.");
    }

    // ===== 6. Camera =====

    private async Task FollowCamera()
    {
        await Chapter(6, "A camera that follows", "Follow the player, and never show past the level's edges.");
        await ui.Select("Main Camera");
        await ui.Say("The camera should follow our hero. That's a Follow component, again with no code.");
        await ui.AddComponent("Follow");
        await ui.SetText("Follow", "Stay Inside", "Level", "Never show past the tilemap's edges");
        await ui.SetVector("Follow", "Dead Zone", 0, 2.5, "Stay still through small jumps");
        await ui.SetNumber("Follow", "Look Ahead", 0.15);
        await ui.SetNumber("Camera", "Size", 6, note: "Half the view's height, in tiles");
        await ui.SetText("Camera", "Background", "#151B2E", "A night sky");
        await ui.Say("Its target is already Player. Stay Inside Level keeps it from ever showing past the edges of the tilemap, and a dead zone keeps it calm while we jump.");
        await ui.FocusEditor();
        await ui.Shortcut(Key.S, ModifierKeys.Control);
    }

    // ===== 7. First playtest =====

    private DemoPilot? _pilot;

    private async Task FirstPlaytest()
    {
        await Chapter(7, "Press play", "Play right in the editor, rewind time, and keep the tweaks you like.");
        await ui.Select("Player");
        await ui.Say("That's enough to play. F5.");
        await ui.FocusEditor();
        await StartPlaying();
        _pilot = new DemoPilot(() => play.Game, Overlay) { Attacks = false, StopAt = 44 };

        var statusSay = ui.Say("The game runs right inside the editor, on a copy of the scene, so nothing I do while playing can hurt my work. The status bar turns orange so you always know you're in play mode.");
        await WaitUntil(() => _pilot.PlayerPosition.X > 14, 9000);
        await statusSay;
        await ui.Say("Jump the pond, up the step, then a double jump through the planks, and another up to the ledge.");
        await WaitUntil(() => _pilot.PlayerPosition.Y > 2.5f, 9000);
        await Task.Delay(Ms(600));

        // Rewind.
        _pilot.Enabled = false;
        await ui.Say("Now a feature I love. F6 pauses, and Griff has been recording the last twenty seconds the whole time.");
        await ui.Shortcut(Key.F6);
        var timeline = DemoUi.Descendants<Slider>(ui.Main.GameTab.Content as DependencyObject ?? ui.Main).First(s => s.Name == "Scrubber");
        var thumb = DemoUi.Descendants<Thumb>(timeline).First();
        ui.Spot(timeline, "Rewind: drag back through time", dim: false);
        var track = DemoUi.ScreenRect(timeline);
        var back = new Point(track.Left + track.Width * 0.18, track.Top + track.Height / 2);
        await ui.SayWhile("Drag the timeline back, and the whole game rewinds. Perfect for catching the frame where a bug happened.", async () =>
        {
            await Input.Drag(DemoUi.Center(thumb), [new Point(track.Left + track.Width * 0.55, back.Y), back], msPerSegment: 900);
            await Input.Pause(400);
            await Input.Drag(back, [new Point(track.Left + track.Width * 0.35, back.Y)], msPerSegment: 700);
        });
        ui.Unspot();
        await ui.Say("Resuming carries on from right here, with a brand new future.");
        await ui.FocusEditor();
        await ui.Shortcut(Key.F6);
        _pilot.Enabled = true;
        await Task.Delay(Ms(1800));

        // Tweak while playing and keep it.
        await ui.Say("While it runs, I can tweak anything in the Inspector. Let's give the jump a little more height.");
        await ui.SetNumber("PlatformerMovement", "Jump Height", 3.8, note: "Changed while playing");
        await ui.Say("Normally, play mode changes are thrown away when you stop. But when you find the perfect value, Control K, Keep Play Changes, copies the selected entity's live values back into your scene.");
        await ui.FocusEditor();
        await ui.Shortcut(Key.K, ModifierKeys.Control);
        await Task.Delay(Ms(800));
        _pilot.Dispose();
        _pilot = null;
        await StopPlaying();
        var row = await DemoUi.WaitFor(() => ui.InspectorRow("PlatformerMovement", "Jump Height"), "jump height");
        await ui.Explain(row, "Kept: 3.8", "Stopped, and the new jump height stuck. Everything else went back to how it was.", dim: false);
    }

    // ===== 8. Coins =====

    private async Task Coins()
    {
        await Chapter(8, "Coins and a score", "Pickups and counters work together with no code.");
        await Deselect();
        await ui.Say("Let's give the player something to collect. I'll make a sprite for the first coin.");
        await ui.Menu("Entity", "Create Sprite");
        await Rename("Coin");
        await ui.SetVector("Transform", "Position", DemoLevel.Coins[0].X, DemoLevel.Coins[0].Y);
        await ui.SetText("Sprite", "Image", "Sprites/coin.png");
        await ui.SetVector("Sprite", "Size", 0.6, 0.6);
        await ui.AddComponent("CircleCollider");
        await ui.SetNumber("CircleCollider", "Radius", 0.3);
        await ui.SetCheck("CircleCollider", "Is Trigger", true, "Triggers detect touches without blocking");
        await ui.Say("A trigger collider notices touches without blocking anything. Then a Pickup component makes it collectible.");
        await ui.AddComponent("Pickup");
        await ui.SetText("Pickup", "Layers", "Player", "Only the player can collect it");
        await ui.SetNumber("Pickup", "Spin", 120, note: "Spins while it waits");
        await ui.Say("When the player touches it, it sends a Score message, then disappears.");

        await ui.Say("Now I need a dozen more. Control D duplicates, and then I just drop each copy where it goes. I'll speed this part up.");
        await ui.LookAt(0, 5, 36, -6, 1000);
        await FastForward(async () =>
        {
            for (var i = 1; i < DemoLevel.Coins.Length; i++)
            {
                await ui.FocusEditor();
                await ui.Shortcut(Key.D, ModifierKeys.Control);
                await ui.SetVector("Transform", "Position", DemoLevel.Coins[i].X, DemoLevel.Coins[i].Y);
            }
        });

        // A HUD counter.
        await Deselect();
        await ui.Say("A Counter adds up those Score messages by itself. I'll put one on a text in the top left corner of the screen.");
        await ui.Menu("Entity", "Create Text");
        await Rename("Score");
        await ui.SetText("Text", "Value", "Coins: 0");
        await ui.SetNumber("Text", "Size", 0.6);
        await ui.SetEnum("Text", "Align", "Left");
        await ui.AddComponent("Counter");
        await ui.SetText("Counter", "Format", "Coins: {0}", "{0} is the count, {1} the best");
        await ui.SetText("Counter", "Save Best As", "coins", "Remember the best run");
        await ui.AddComponent("ScreenAnchor");
        await ui.SetVector("ScreenAnchor", "Offset", 0.6, -0.6, "Pinned to the top-left");
        await ui.Say("A Screen Anchor pins it to the top left, whatever shape the window is, and wherever the camera goes.");
        await ui.FocusEditor();
        await ui.Shortcut(Key.S, ModifierKeys.Control);
    }

    // ===== 9. Enemies =====

    private async Task Enemies()
    {
        await Chapter(9, "Slimes and a sword", "Enemies that patrol and hurt, health that respawns, and a way to fight back.");
        await Deselect();
        await ui.Say("A level needs some danger. Let's build a slime from parts.");
        await ui.Menu("Entity", "Create Sprite");
        await Rename("Slime");
        await ui.SetVector("Transform", "Position", DemoLevel.Slime1.X, DemoLevel.Slime1.Y);
        await ui.SetText("Sprite", "Image", "Sprites/slime.png");
        await ui.SetNumber("Sprite", "Columns", 2);
        await ui.AddComponent("Animation");
        await ui.SetNumber("Animation", "Frame Count", 2);
        await ui.SetNumber("Animation", "Frames Per Second", 4);

        await ui.Say("A box collider on the Enemy layer, and a Body, so gravity and walls affect it.");
        await FastForward(async () =>
        {
            await ui.AddComponent("BoxCollider");
            await ui.SetVector("BoxCollider", "Size", 0.8, 0.55);
            await ui.SetVector("BoxCollider", "Offset", 0, -0.22);
            await ui.SetText("BoxCollider", "Layer", "Enemy");
            await ui.AddComponent("Body");
        });

        await ui.Say("Patrol walks it back and forth, turning at walls and ledges. Damage On Touch hurts the player and knocks them back. And Health means our sword can defeat it in two hits.");
        await ui.AddComponent("Patrol");
        await ui.SetNumber("Patrol", "Speed", 1.5);
        await ui.AddComponent("DamageOnTouch");
        await ui.SetText("DamageOnTouch", "Layers", "Player", "Hurts the player");
        await ui.SetNumber("DamageOnTouch", "Knockback", 8);
        await ui.AddComponent("Health");
        await ui.SetNumber("Health", "Max", 2);
        await ui.SetNumber("Health", "Current", 2);

        await ui.Look(30, -2, 4);
        await ui.Say("Control D, and there's a second slime, further along.");
        await ui.FocusEditor();
        await ui.Shortcut(Key.D, ModifierKeys.Control);
        await ui.SetVector("Transform", "Position", DemoLevel.Slime2.X, DemoLevel.Slime2.Y);

        // Player health and the sword.
        await ui.Select("Player");
        await ui.Say("Now the player needs health too. Three hearts, and with Respawn on, falling or losing them all sends you back to the last checkpoint.");
        await ui.AddComponent("Health");
        await ui.SetNumber("Health", "Max", 3);
        await ui.SetNumber("Health", "Current", 3);
        await ui.SetCheck("Health", "Respawn", true, "Back at the last checkpoint");
        await ui.SetNumber("Health", "Invincible Time", 1);

        await ui.Say("To fight back, Entity, Add Attack gives the player a swing on the Attack key, which is X. It adds a hitbox that only exists while swinging. I'll give the hitbox a slash sprite so we can see it.");
        await ui.Menu("Entity", "Add Attack", "Entity → Add Attack");
        await ui.AddComponent("Sprite");
        await ui.SetText("Sprite", "Image", "Sprites/slash.png");
        await ui.SetVector("Sprite", "Size", 1.4, 0.7);

        await Deselect();
        await ui.Say("Then a health bar. Create Bar, set it to track Player, and it follows the player's health on its own.");
        await FastForward(async () =>
        {
            await ui.Menu("Entity", "Create Bar");
            await Rename("Hearts");
            await ui.SetText("Bar", "Track", "Player");
            await ui.SetVector("Bar", "Size", 2.4, 0.3);
            await ui.AddComponent("ScreenAnchor");
            await ui.SetVector("ScreenAnchor", "Offset", 1.8, -1.35);
        });

        await Deselect();
        await ui.Say("Falling in the water should cost a life. A wide trigger under each pond, with Damage On Touch set to Kill, does it.");
        await FastForward(async () =>
        {
            for (var i = 0; i < DemoLevel.Ponds.Length; i++)
            {
                var (at, size) = DemoLevel.Ponds[i];
                if (i == 0)
                {
                    await ui.Menu("Entity", "Create Empty");
                    await Rename("Pond");
                    await ui.SetVector("Transform", "Position", at.X, at.Y);
                    await ui.AddComponent("BoxCollider");
                    await ui.SetVector("BoxCollider", "Size", size.X, size.Y);
                    await ui.SetCheck("BoxCollider", "Is Trigger", true);
                    await ui.AddComponent("DamageOnTouch");
                    await ui.SetText("DamageOnTouch", "Layers", "Player");
                    await ui.SetCheck("DamageOnTouch", "Kill", true);
                }
                else
                {
                    await ui.FocusEditor();
                    await ui.Shortcut(Key.D, ModifierKeys.Control);
                    await ui.SetVector("Transform", "Position", at.X, at.Y);
                }
            }

            // A checkpoint flag past the ledge.
            await Deselect();
            await ui.Menu("Entity", "Create Sprite");
            await Rename("Checkpoint");
            await ui.SetVector("Transform", "Position", DemoLevel.Checkpoint.X, DemoLevel.Checkpoint.Y);
            await ui.SetText("Sprite", "Image", "Sprites/flag.png");
            await ui.SetVector("Sprite", "Size", 1, 2);
            await ui.AddComponent("BoxCollider");
            await ui.SetVector("BoxCollider", "Size", 0.8, 2);
            await ui.SetCheck("BoxCollider", "Is Trigger", true);
            await ui.AddComponent("Checkpoint");
        });
        await ui.Say("And a checkpoint flag after the ledge, so a slip near the end doesn't send you all the way back.");
        await ui.FocusEditor();
        await ui.Shortcut(Key.S, ModifierKeys.Control);
    }

    // ===== 10. Script =====

    private const string HoverPath = "Scripts/Hover.cs";

    private const string HoverCode = """
        public class Hover : Behavior
        {
            public float Height { get; set; } = 0.25f;
            public float Speed { get; set; } = 3;

            private Vector3 _start;

            protected override void Start() => _start = Transform.Position;

            protected override void Update()
            {
                var bob = MathF.Sin((float)Time.Total * Speed) * Height;
                Transform.Position = _start + new Vector3(0, bob, 0);
            }
        }
        """;

    private const string HoverFrom = "MathF.Sin((float)Time.Total * Speed) * Height";
    private const string HoverTo = "MathF.Abs(MathF.Sin((float)Time.Total * Speed)) * Height * 3";

    private async Task KeyScript()
    {
        await Chapter(10, "Your own code", "Scripts are C#. Save, and the running game picks them up.");
        await Deselect();
        await ui.Say("So far, no code at all. But sometimes you want something of your own. Let's put a key on the high ledge, and make it float with a script.");
        await ui.Menu("Entity", "Create Sprite");
        await Rename("Key");
        await ui.SetVector("Transform", "Position", DemoLevel.Key.X, DemoLevel.Key.Y);
        await ui.SetText("Sprite", "Image", "Sprites/key.png");
        await ui.SetVector("Sprite", "Size", 0.8, 0.8);
        await ui.Look(DemoLevel.Key.X, DemoLevel.Key.Y - 1, 4);

        var code = HoverCode.ReplaceLineEndings("\n");
        var card = CodeCardRect();
        await ui.Say("A script is a C# class that derives from Behavior. In your code editor, it looks like this.");
        var typing = Overlay.TypeCode(card, "Assets/Scripts/Hover.cs", code, options.Fast ? 2 : 24);
        await ui.Say("Public properties, like Height and Speed, show up in the Inspector and are saved with the scene. Update runs every frame, and here it bobs the key up and down along a sine wave.");
        await typing;
        var path = Path.Combine(workspace.Project!.AssetsPath, HoverPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, HoverCode.ReplaceLineEndings(Environment.NewLine));
        Overlay.ShowKeys("Ctrl + S");
        await WaitUntil(() => workspace.Components.Find("Hover") is not null, 15000);
        await DemoUi.Settle(300);
        Overlay.HideCode();
        await ui.Say("The moment it's saved, Griff compiles it, and Hover is a component like any other.");
        await ui.AddComponent("Hover");
        var hover = await DemoUi.WaitFor(() => ui.ComponentHeader("Hover"), "the Hover section");
        hover.BringIntoView();
        await DemoUi.Settle(150);
        await ui.Explain(hover, "Your script, in the Inspector", "There are its properties, ready to tweak.", dim: false);

        await ui.Say("Then a trigger collider and a Pickup with an Item, so collecting it puts a Key in the player's inventory.");
        await FastForward(async () =>
        {
            await ui.AddComponent("CircleCollider");
            await ui.SetNumber("CircleCollider", "Radius", 0.4);
            await ui.SetCheck("CircleCollider", "Is Trigger", true);
            await ui.AddComponent("Pickup");
            await ui.SetText("Pickup", "Layers", "Player");
            await ui.SetText("Pickup", "Message", "");
            await ui.SetText("Pickup", "Item", "Key");
        });

        // Hot reload while playing.
        await ui.FocusEditor();
        await ui.Shortcut(Key.S, ModifierKeys.Control);
        await ui.Say("Now watch this. I'll press play, and look at the key in the Scene view, which stays live while the game runs.");
        await StartPlaying();
        await DemoUi.Settle(400);
        await ui.Click(DemoUi.Descendants<TabItem>(ui.Main.DocumentTabs).First(t => t == ui.Main.SceneTab));
        await DemoUi.Settle(200);
        await ui.Look(DemoLevel.Key.X, DemoLevel.Key.Y, 2.6f, 700);
        var keyRect = ui.WorldRect(DemoLevel.Key.X - 1.6f, DemoLevel.Key.Y + 1.4f, DemoLevel.Key.X + 1.6f, DemoLevel.Key.Y - 1.4f);
        ui.Spot(keyRect, "Bobbing, live", dim: false);
        await ui.Say("There it goes, bobbing gently. Now I'll change the code while the game is still running.");
        ui.Unspot();
        Overlay.CodeTitle("Assets/Scripts/Hover.cs");
        var cardCode = Overlay.TypeCode(CodeCardRect(), "Assets/Scripts/Hover.cs", code, 1);
        await cardCode;
        await Overlay.EditCode(code, HoverFrom, HoverTo, options.Fast ? 3 : 45);
        await Task.Delay(Ms(400));
        File.WriteAllText(path, HoverCode.Replace(HoverFrom, HoverTo).ReplaceLineEndings(Environment.NewLine));
        Overlay.ShowKeys("Ctrl + S");
        await ui.Say("Save, and every Hover in the running game is swapped for the new version in a moment, without restarting. Now it bounces like a ball.");
        Overlay.HideCode();
        ui.Spot(keyRect, "Hot reloaded: bouncing", dim: false);
        await Task.Delay(Ms(2500));
        ui.Unspot();
        await ui.Say("If the code doesn't compile, the last good version keeps running and the Output panel points at the exact line. Let's keep the bounce, and stop.");
        await ui.FocusEditor();
        await StopPlaying();
    }

    // ===== 11. Door =====

    private async Task LockedDoor()
    {
        await Chapter(11, "A locked door", "Keys, locks and the way out, with no code.");
        await Deselect();
        await ui.Say("The key needs a door. A Door component goes somewhere when the player walks into it. Give it a Key, and it won't open until the player has one in their inventory.");
        await ui.Menu("Entity", "Create Sprite");
        await Rename("Door");
        await ui.SetVector("Transform", "Position", DemoLevel.Door.X, DemoLevel.Door.Y);
        await ui.SetText("Sprite", "Image", "Sprites/door.png");
        await ui.SetVector("Sprite", "Size", 1, 2);
        await ui.SetNumber("Sprite", "Order", -1);
        await ui.AddComponent("BoxCollider");
        await ui.SetVector("BoxCollider", "Size", 0.8, 2);
        await ui.SetCheck("BoxCollider", "Is Trigger", true);
        await ui.AddComponent("Door");
        await ui.SetText("Door", "Scene", "Win", "Loads the Win scene");
        await ui.SetText("Door", "Key", "Key", "Won't open without a Key");
        await ui.SetNumber("Door", "Fade", 0.6);
        await ui.Look(DemoLevel.Door.X - 2, -2, 4);
        await ui.Say("It'll load a Win scene, which we'll make in a minute, with a fade on the way.");
        await ui.FocusEditor();
        await ui.Shortcut(Key.S, ModifierKeys.Control);
    }

    // ===== 12. Atmosphere =====

    private async Task Atmosphere()
    {
        await Chapter(12, "Atmosphere", "Darkness, flickering torches, a lantern and parallax hills.");
        await ui.Select("Main Camera");
        await ui.Say("It works, but it could feel a lot moodier. The camera's Ambient color dims the whole scene, as if it's dusk.");
        await ui.SetText("Camera", "Ambient", "#5A6490", "Dim everything...");

        await ui.Select("Player");
        await ui.Say("Then lights brighten the dark. A warm Light on the player works like a lantern.");
        await ui.AddComponent("Light");
        await ui.SetNumber("Light", "Radius", 5);
        await ui.SetNumber("Light", "Intensity", 0.9);

        await Deselect();
        await ui.Say("And a few torches along the way, with a little flicker.");
        await FastForward(async () =>
        {
            for (var i = 0; i < DemoLevel.Torches.Length; i++)
            {
                var at = DemoLevel.Torches[i];
                if (i == 0)
                {
                    await ui.Menu("Entity", "Create Light");
                    await Rename("Torch");
                    await ui.SetVector("Transform", "Position", at.X, at.Y);
                    await ui.SetNumber("Light", "Radius", 6);
                    await ui.SetNumber("Light", "Flicker", 0.25);
                    await ui.AddComponent("Sprite");
                    await ui.SetText("Sprite", "Image", "Sprites/torch.png");
                    await ui.SetNumber("Sprite", "Columns", 2);
                    await ui.SetVector("Sprite", "Size", 0.8, 0.8);
                    await ui.AddComponent("Animation");
                    await ui.SetNumber("Animation", "Frame Count", 2);
                    await ui.SetNumber("Animation", "Frames Per Second", 6);
                }
                else
                {
                    await ui.FocusEditor();
                    await ui.Shortcut(Key.D, ModifierKeys.Control);
                    await ui.SetVector("Transform", "Position", at.X, at.Y);
                }
            }

            await Deselect();
            await ui.Menu("Entity", "Create Sprite");
            await Rename("Hills");
            await ui.SetVector("Transform", "Position", 32, -2);
            await ui.SetText("Sprite", "Image", "Sprites/hills.png");
            await ui.SetVector("Sprite", "Size", 24, 9);
            await ui.SetNumber("Sprite", "Order", -10);
            await ui.AddComponent("Parallax");
            await ui.SetVector("Parallax", "Factor", 0.35, 0.8);
            await ui.SetCheck("Parallax", "Repeat X", true);
        });
        await ui.Say("Finally, distant hills with Parallax, which drift slower than the level as the camera moves, and repeat forever.");
        await ui.FocusEditor();
        await ui.Shortcut(Key.S, ModifierKeys.Control);
        await ShowLevel("Ember Hollow, lit");
    }

    private async Task ShowLevel(string label)
    {
        await Deselect();
        await ui.LookAt(0, DemoLevel.Top, DemoLevel.Width, DemoLevel.Top - DemoLevel.Height, 1400);
        await ui.Explain(ui.WorldRect(0, DemoLevel.Top, DemoLevel.Width, DemoLevel.Top - DemoLevel.Height), label, "Here's the whole level now.", dim: false);
    }

    // ===== 13. Menus =====

    private async Task Menus()
    {
        await Chapter(13, "Menus", "A title screen and a win screen: buttons that need no code.");
        await ui.Say("A real game needs a title screen. File, New Scene.");
        await ui.Menu("File", "New Scene...", "File → New Scene");
        await TypeIntoPrompt("Menu");
        await DemoUi.WaitFor(() => workspace.Scene?.Name == "Menu" ? workspace.Scene : null, "the Menu scene");
        await DemoUi.Settle(300);

        await ui.Say("A big title, and a Play button. A button's Action can load a scene, restart, quit or open a panel, all without a line of code.");
        await ui.Menu("Entity", "Create Text");
        await Rename("Title");
        await ui.SetText("Text", "Value", "EMBER HOLLOW");
        await ui.SetNumber("Text", "Size", 1.4);
        await ui.SetText("Text", "Color", "#FFB547");
        await ui.SetNumber("Transform", "Position", 1.6, "Y");

        await Deselect();
        await ui.Menu("Entity", "Create Button");
        await Rename("Play");
        await ui.SetText("Text", "Value", "Play");
        await ui.SetNumber("Transform", "Position", -1, "Y");
        await ui.SetEnum("Button", "Action", "LoadScene");
        await ui.SetText("Button", "Scene", "Main", "The level");
        await ui.SetNumber("Button", "Fade", 0.5, note: "Fade out and back in");
        await ui.SetEnum("Button", "Shortcut", "Enter");
        await ui.Say("Load Scene Main, with a half second fade, and Enter as a shortcut. Buttons work with the mouse, the keyboard, or a controller.");

        await FastForward(async () =>
        {
            await Deselect();
            await ui.Menu("Entity", "Create Text");
            await Rename("Hint");
            await ui.SetText("Text", "Value", "Find the key. Reach the door.");
            await ui.SetNumber("Text", "Size", 0.45);
            await ui.SetText("Text", "Color", "#9AA3B8");
            await ui.SetNumber("Transform", "Position", 0.2, "Y");
            await ui.Select("Main Camera");
            await ui.SetText("Camera", "Background", "#151B2E");
        });
        await ui.FocusEditor();
        await ui.Shortcut(Key.S, ModifierKeys.Control);

        // Win scene.
        await ui.Say("And a Win scene for when you make it through the door. I'll speed through this one, since it's the same idea.");
        await FastForward(async () =>
        {
            await ui.Menu("File", "New Scene...");
            await TypeIntoPrompt("Win");
            await DemoUi.WaitFor(() => workspace.Scene?.Name == "Win" ? workspace.Scene : null, "the Win scene");
            await DemoUi.Settle(200);
            await ui.Menu("Entity", "Create Text");
            await Rename("Title");
            await ui.SetText("Text", "Value", "YOU ESCAPED!");
            await ui.SetNumber("Text", "Size", 1.2);
            await ui.SetText("Text", "Color", "#7BD35A");
            await ui.SetNumber("Transform", "Position", 1.8, "Y");
            await Deselect();
            await ui.Menu("Entity", "Create Text");
            await Rename("Best");
            await ui.SetText("Text", "Value", "Most coins: 0");
            await ui.SetNumber("Text", "Size", 0.55);
            await ui.SetNumber("Transform", "Position", 0.6, "Y");
            await ui.AddComponent("Counter");
            await ui.SetText("Counter", "Message", "");
            await ui.SetText("Counter", "Format", "Most coins: {1}");
            await ui.SetText("Counter", "Save Best As", "coins");
            await Deselect();
            await ui.Menu("Entity", "Create Button");
            await Rename("Again");
            await ui.SetText("Text", "Value", "Play again");
            await ui.SetNumber("Transform", "Position", -1.2, "Y");
            await ui.SetEnum("Button", "Action", "LoadScene");
            await ui.SetText("Button", "Scene", "Menu");
            await ui.SetNumber("Button", "Fade", 0.5);
            await ui.SetEnum("Button", "Shortcut", "Enter");
            await ui.Select("Main Camera");
            await ui.SetText("Camera", "Background", "#151B2E");
            await ui.FocusEditor();
            await ui.Shortcut(Key.S, ModifierKeys.Control);
        });
        await ui.Say("Its counter shows the most coins you've ever collected, which the level's coin counter saves for us.");

        // Start scene.
        await ui.Say("Last, the game should start on the menu. Right click the Menu scene in Assets, Set as Start Scene.");
        TreeViewItem? menuScene = null;
        try
        {
            await ExpandAsset("Scenes");
            menuScene = await DemoUi.WaitFor(() => DemoUi.Descendants<TreeViewItem>(ui.PanelContent("Assets")).FirstOrDefault(i => i.DataContext is AssetNode n && n.Name.StartsWith("Menu", StringComparison.Ordinal) && i.IsVisible), "Menu.scene", 4000);
        }
        catch (InvalidOperationException)
        {
            Log("FORCED start scene (Menu.scene not found in Assets)");
            var path = workspace.Project!.FindScene("Menu")!;
            await workspace.SetStartSceneAsync(path);
            await workspace.SaveSceneAsync();
            await workspace.OpenSceneAsync(path);
            return;
        }

        await ui.Click(DemoUi.Descendants<TextBlock>(menuScene).First(), right: true);
        var item = await DemoUi.WaitFor(() => DemoUi.MenuItem("Set as Start Scene"), "Set as Start Scene");
        await DemoUi.Settle(150);
        await ui.Click(item);
        await DemoUi.Settle(200);
        // Setting the start scene refreshes the Assets tree, so find the row again before opening it.
        try
        {
            var row = await DemoUi.WaitFor(() => DemoUi.Descendants<TreeViewItem>(ui.PanelContent("Assets")).FirstOrDefault(i => i.DataContext is AssetNode n && n.Name.StartsWith("Menu", StringComparison.Ordinal) && i.IsVisible && PresentationSource.FromVisual(i) is not null), "Menu.scene again", 3000);
            await Input.Click(DemoUi.Center(DemoUi.Descendants<TextBlock>(row).First()), clicks: 2);
            await WaitUntil(() => workspace.Scene?.Name == "Menu", 3000);
        }
        catch (InvalidOperationException)
        {
        }

        if (workspace.Scene?.Name != "Menu")
        {
            Log("FORCED open Menu scene");
            await workspace.SaveSceneAsync();
            await workspace.OpenSceneAsync(workspace.Project!.FindScene("Menu")!);
        }

        await DemoUi.WaitFor(() => workspace.Scene?.Name == "Menu" ? workspace.Scene : null, "the Menu scene reopened");
        await DemoUi.Settle(300);
    }

    private async Task TypeIntoPrompt(string text)
    {
        var prompt = await DemoUi.WaitFor(() => Application.Current.Windows.OfType<Core.Dialogs.PromptWindow>().FirstOrDefault(w => w.IsVisible), "the prompt");
        await DemoUi.Settle(200);
        Overlay.BringToFront();
        var box = (TextBox)prompt.FindName("Input");
        await Input.Click(DemoUi.Center(box));
        await Input.Press(Key.A, ModifierKeys.Control, showBadge: false);
        await Input.Type(text, 70);
        await Input.Press(Key.Enter, showBadge: false);
        await DemoUi.Settle(300);
    }

    // ===== 14. Play it through =====

    private async Task PlayThrough()
    {
        await Chapter(14, "Play it through", "From the title screen to the door.");
        await ui.Say("That's the whole game. Let's play it from the title screen. F5.");
        await ui.FocusEditor();
        await StartPlaying();
        await Task.Delay(Ms(1500));
        await ui.Say("Enter to start.");
        if (play.Game?.Input is Griff.Runtime.InputState menuInput)
        {
            Overlay.ShowKeys("Enter");
            menuInput.SetKey(Griff.Runtime.Key.Enter, true);
            await Task.Delay(80);
            menuInput.SetKey(Griff.Runtime.Key.Enter, false);
        }

        await WaitUntil(() => play.Game?.SceneName == "Main", 6000);
        await Task.Delay(Ms(700));
        _pilot = new DemoPilot(() => play.Game, Overlay);
        var pilot = _pilot;

        await ui.Say("Coins first. Watch the counter in the corner pop as they're collected.");
        await WaitUntil(() => pilot.PlayerPosition.X > 25, 15000);
        await ui.Say("Here comes the first slime. X to swing the sword.");
        await WaitUntil(() => pilot.PlayerPosition.Y > 2.5f || play.Game?.SceneName != "Main", 20000);
        await ui.Say("Up to the ledge, and there's our bouncing key.");
        await WaitUntil(() => pilot.PlayerPosition.X > 44, 15000);
        await ui.Say("Checkpoint. One more slime, one more pond, and the door.");
        await WaitUntil(() => play.Game?.SceneName == "Win", 30000);
        await Task.Delay(Ms(1200));
        pilot.Dispose();
        _pilot = null;
        await ui.Say("We escaped! And the win screen remembers our best coin count.");
        await Task.Delay(Ms(800));
        await ui.FocusEditor();
        await StopPlaying();
    }

    // ===== 15. Ship =====

    private async Task Ship()
    {
        await Chapter(15, "Ship it", "One click makes a standalone game you can zip and upload.");
        await ui.Say("Finally, let's ship it. Game, Build Game, or Control B, exports the whole game as one exe file. Players don't need to install anything.");
        await ui.FocusEditor();
        var buildFolder = Path.Combine(workspace.Project!.RootPath, "Build");
        await ui.Menu("Game", "Build Game", "Build Game (Ctrl+B)");
        // The game lands in a folder of its own under Build, next to the runtime's helpers (createdump.exe and so on).
        var exe = await DemoUi.WaitFor(() => Directory.Exists(buildFolder)
            ? Directory.GetFiles(buildFolder, ProjectName + ".exe", SearchOption.AllDirectories).FirstOrDefault()
            : null, "the built game", 120000);
        buildFolder = Path.GetDirectoryName(exe)!;
        await Task.Delay(Ms(2500));
        await ui.Say("There it is in Explorer: Ember Hollow dot exe, ready to zip up and post on itch dot io. I'll put a copy on the desktop, and run it from there.");
        CloseExplorerAt(buildFolder);

        // The finished game is one file; it goes on the desktop. A file there that Griff didn't build is never replaced.
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var copy = Path.Combine(desktop, Path.GetFileName(exe));
        if (File.Exists(copy) && !Griff.Runtime.GamePackage.IsPackaged(copy))
            copy = Path.Combine(desktop, ProjectName + " (Griff demo).exe");
        File.Copy(exe, copy, overwrite: true);
        exe = copy;
        buildFolder = desktop;
        Log($"exported to {copy}");
        await Task.Delay(Ms(500));

        Process? game = null;
        try
        {
            game = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = buildFolder, UseShellExecute = true });
            var window = await WaitForWindow(game!, 20000);
            if (window != IntPtr.Zero)
            {
                SetForegroundWindow(window);
                await Task.Delay(Ms(2200));
                await ui.Say("Our title screen, running on its own. Enter to play.");
                await Input.Press(Key.Enter);
                await Task.Delay(1500);
                Overlay.ShowKeys("→");
                Input.KeyDown(Key.Right);
                await Task.Delay(900);
                await Input.Press(Key.Space, showBadge: true);
                await Task.Delay(1800);
                Input.KeyUp(Key.Right);
                await ui.Say("Same game, same feel, no editor required.");
                await Task.Delay(Ms(600));
            }
        }
        finally
        {
            Input.KeyUp(Key.Right);
            try
            {
                if (game is { HasExited: false })
                {
                    game.CloseMainWindow();
                    if (!game.WaitForExit(3000))
                        game.Kill();
                }
            }
            catch (InvalidOperationException)
            {
            }

            ui.Main.Activate();
        }
    }

    // ===== Outro =====

    private async Task Outro()
    {
        await DemoUi.Settle(400);
        Overlay.ShowCard("That's Ember Hollow", "Your turn", "Tilemaps · no-code players, enemies, pickups and doors · C# scripts with hot reload · rewind · lighting · menus · one-click builds");
        await ui.Say("And that's it. In one sitting we went from an empty project to a finished, shipped platformer: a painted level, a hero, coins, slimes, a sword, a key and a locked door, our own script with live reloading, lighting, menus, and a standalone exe.");
        await ui.Say("Griff has much more to explore, like pause menus, dialogue, top-down movement, pathfinding, and particles. Start from a template, and see what you can make by Sunday night. Happy jamming!");
        await Task.Delay(Ms(1500));
        Overlay.HideCard();
    }

    // ===== Helpers =====

    private TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(Math.Max(1, ms / (options.Fast ? 4 : 1)));

    private async Task FastForward(Func<Task> body)
    {
        var speed = Input.Speed;
        Input.Speed = speed * 3.5;
        Overlay.Badge("⏩  Sped up");
        try
        {
            await body();
        }
        finally
        {
            Input.Speed = speed;
            Overlay.Badge(null);
        }
    }

    private async Task Rename(string name)
    {
        var box = await DemoUi.WaitFor(() => DemoUi.Descendants<TextBox>(ui.Inspector).FirstOrDefault(b => b.DataContext is EntityHeader), "the name field");
        await ui.EditBox(box, name);
    }

    private async Task StartPlaying()
    {
        await ui.FocusEditor();
        await ui.Shortcut(Key.F5);
        await WaitUntil(() => play.IsPlaying, 1500);
        if (!play.IsPlaying)
        {
            Log("retry play via toolbar");
            await ui.Click(ui.ToolbarButton("Game.Play"));
            await WaitUntil(() => play.IsPlaying, 1500);
        }

        if (!play.IsPlaying)
        {
            Log("FORCED play");
            play.Play();
        }

        await DemoUi.Settle(200);
    }

    private async Task StopPlaying()
    {
        await ui.FocusEditor();
        await ui.Shortcut(Key.F5, ModifierKeys.Shift);
        await WaitUntil(() => !play.IsPlaying, 1500);
        if (play.IsPlaying)
        {
            Log("retry stop via toolbar");
            await ui.Click(ui.ToolbarButton("Game.Stop"));
            await WaitUntil(() => !play.IsPlaying, 1500);
        }

        if (play.IsPlaying)
        {
            Log("FORCED stop");
            play.Stop();
        }

        await DemoUi.Settle(200);
    }

    /// <summary>Selects nothing, so new entities go at the root of the scene rather than inside the selection.</summary>
    private async Task Deselect()
    {
        if (workspace.Scene is not null && selection.Entity is not null)
            selection.Select(null);
        await DemoUi.Settle();
    }

    private Rect CodeCardRect()
    {
        var scene = DemoUi.ScreenRect(sceneView);
        var width = Math.Min(scene.Width * 0.78, 1500);
        return new Rect(scene.Left + (scene.Width - width) / 2, scene.Top + 30, width, scene.Height * 0.7);
    }

    private async Task WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(50);
        Log($"wait {(condition() ? "met" : "TIMED OUT")} after {clock.ElapsedMilliseconds} ms; player at {_pilot?.PlayerPosition}");
    }

    private void Log(string message) => options.Log?.Invoke(message);

    private static async Task<IntPtr> WaitForWindow(Process process, int timeoutMs)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            process.Refresh();
            if (process.HasExited)
                return IntPtr.Zero;
            if (process.MainWindowHandle != IntPtr.Zero)
                return process.MainWindowHandle;
            await Task.Delay(100);
        }

        return IntPtr.Zero;
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
    }

    private static void CloseExplorerAt(string folder)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
                return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            foreach (var window in shell.Windows())
            {
                try
                {
                    string? path = window.Document?.Folder?.Self?.Path;
                    if (path is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
                        window.Quit();
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception)
        {
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}

internal sealed record DemoOptions(bool Fast, Action<string>? Log);
#endif
