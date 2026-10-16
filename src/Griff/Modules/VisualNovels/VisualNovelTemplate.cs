using System.Numerics;
using Griff.Audio;
using Griff.Input;
using Griff.Projects;
using Griff.Scenes;
using Griff.Templates;
using static Griff.Templates.ProjectTemplates;

namespace Griff.Modules.VisualNovels;

// The Visual Novel template: "Starfall Café", a short story with no code. A title screen (New Game, Continue, Load,
// Settings, Quit), and a Story scene on a stage: June closes her café for the night when a little star, Comet, falls into
// the flour; Felix, who saw it fall past his telescope, comes running. How June warms Comet up (cocoa or the bread oven)
// is a flag, and whether she sends it home or asks it to stay picks one of three endings. The reading menus come with
// it: Log, Skip, Auto, quick saves, save and load screens and settings.
internal sealed class VisualNovelTemplate : ProjectTemplate
{
    public override string Title => "Visual Novel";

    public override string Description => "Starfall Café: a little star falls into the flour at closing time, and June and Felix have until sunrise to send it home. Three characters with faces that change as they speak, a café, a roof under the stars and a dawn, two choices and three endings, with save and load slots, a log, skip, auto and settings, and no code. Click, tap, Space or A to read on; arrows and Enter to choose.";

    public override int Order => 129;

    protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Chill, seed: 29) with { Volume = 0.4f };

    protected override Scene BuildStartScene() => TitleScreen();

    protected override Dictionary<string, Scene> BuildMoreScenes() => new() { [$"Scenes/{StoryScene}.scene"] = Story() };

    public override IReadOnlyDictionary<string, byte[]> Images() => VisualNovelArt.Files();

    // The lines type with a soft tick, and the endings chime.
    protected internal override void WireSounds(Entity entity)
    {
        foreach (var component in entity.Components)
        {
            switch (component)
            {
                case Typewriter typing:
                    TemplateSounds.Set(typing.Sound, s => typing.Sound = s, TemplateSounds.Dig);
                    break;
                case Panel panel when Endings.Any(e => e.Panel == entity.Name):
                    TemplateSounds.Set(panel.Sound, s => panel.Sound = s, TemplateSounds.PowerUp);
                    break;
            }
        }

        base.WireSounds(entity);
    }

    /// <summary>The scene the story is told in.</summary>
    public const string StoryScene = "Story";

    /// <summary>The entity the stage, its dialogue and the cast are on.</summary>
    public const string StageName = "Stage";

    /// <summary>The flags the first choice sets: how June warms Comet up.</summary>
    public const string CocoaFlag = "Cocoa", OvenFlag = "Oven";

    /// <summary>The three endings: each one's panel (opened by the message the story sends, its name without the space), title and what happened.</summary>
    public static readonly (string Panel, string Title, string Blurb)[] Endings =
    [
        ("Home Ending", "Home in the Sky", "Comet went home, full of cocoa, and twinkles over the café every night."),
        ("Loaf Ending", "The Loaf Constellation", "Comet went home smelling of fresh bread, and the sky has a new shape in it."),
        ("Stay Ending", "Starfall Café", "Comet stayed, and the café on the hill has its own star."),
    ];

    /// <summary>
    /// The story, in a dialogue's lines: tags at the start of a line direct the stage, "Name (face):" says who speaks and
    /// how they look, "&gt;" offers a choice and "#" starts a part the choices go to.
    /// </summary>
    public static IReadOnlyList<string> Script { get; } =
    [
        "[background Cafe cut]",
        "Closing time at the little café on the hill. Rain taps at the window.",
        "[show June center happy fade] June: Cups washed, chairs up, ovens off. Another good day.",
        "[send Crash] CRASH!",
        "[move June left] June (surprised): That came from the flour sacks!",
        "[show Comet right sleepy fade] Something small is sitting in the flour, glowing and blinking.",
        "Comet (sleepy): Oof. Is this the sky? It smells like bread.",
        "June (surprised): You're a star! A real, fallen-down star!",
        "Comet (sad): I slipped off the edge of the night. Now I'm cold, and my glow's gone all dim.",
        "[send Bell] [show Felix center slide] Felix (flustered): Sorry! Sorry! Something fell right past my telescope. Did it land in here?",
        "Felix (happy): It did! A real star! This is the best night of my whole life.",
        "Felix: Stars have to be home before sunrise. It'll need to be warm and bright to fly that far.",
        "June (happy): Then let's warm you up, little one.",
        $"> [set {CocoaFlag}] Make it a mug of hot cocoa -> Cocoa",
        $"> [set {OvenFlag}] Tuck it into the warm bread oven -> Oven",
        "# Cocoa",
        "June (happy): Hot cocoa, extra marshmallows. Do stars like marshmallows?",
        "[send Twinkle] Comet (happy): I don't know what they are, but I love them! I'm all fizzy inside!",
        "-> Roof",
        "# Oven",
        "June: The oven's still warm from the morning loaves. In you go, just for a minute.",
        "[hide Comet fade] Felix (flustered): Is it safe to bake a star?",
        "[send Twinkle] [show Comet right happy fade] Comet (happy): That was lovely! I smell like toast, and I'm glowing again!",
        "-> Roof",
        "# Roof",
        "[hide all] [background Roof slide] They climb out onto the café roof. The rain has gone, and the sky is full of stars.",
        "[show June left] [show Felix right] [show Comet center happy] Felix: There, between the two bright ones. That gap is where you belong.",
        "Comet (sad): It's very far. And it's nice down here, with the warm bread and the both of you.",
        "June (sad): We'll miss you too.",
        "> \"Go on home. The sky needs you.\" -> Home",
        "> \"You could always stay here with us.\" -> Stay",
        "# Home",
        "June (happy): Go on home. Come and wave at us sometimes?",
        "Comet (happy): Every night. I promise!",
        "[move Comet sky] Up it goes, higher and higher, until it's a twinkle in the gap.",
        $"[if {OvenFlag}] -> Loaf",
        "[hide Comet fade] [background Dawn fade] Felix (happy): Look! It's the brightest star in the sky. That's the marshmallows, I bet.",
        "June (happy): And it started out in my flour sack.",
        "[send HomeEnding]",
        "-> End",
        "# Loaf",
        "[hide Comet fade] [background Dawn fade] Felix (happy): Look, the stars round it have made a new shape. It's a loaf of bread!",
        "June (happy): A bread constellation. I'm putting that on the menu.",
        "[send LoafEnding]",
        "-> End",
        "# Stay",
        "Comet (surprised): Stay? Me? Here?",
        "June (happy): There's a spot by the window that needs a light. And you'd get first pick of the bread.",
        $"[if {CocoaFlag}] Comet (happy): And cocoa? Every night?",
        $"[if {OvenFlag}] Comet (happy): And naps in the oven?",
        "June (happy): Every night.",
        "[hide all] [background Cafe fade] The next morning, the café has a new sign over the door.",
        "[show June left happy] [show Comet center happy] [show Felix right happy] Felix: The Starfall Café. The only café in town with its own star.",
        "[send StayEnding]",
        "-> End",
    ];

    private static readonly Color Gold = Color.FromBytes(0xF2, 0xC9, 0x4C);
    private static readonly Color Light = Color.FromBytes(0xF4, 0xEC, 0xDC);
    private static readonly Color Dim = Color.FromBytes(0xB8, 0xBC, 0xD8);
    private static readonly Color Night = Color.FromBytes(0x0E, 0x14, 0x38);
    private static readonly Color Plum = Color.FromBytes(0x6A, 0x4A, 0x9A);
    private static readonly Color Gray = Color.FromBytes(0x3A, 0x3F, 0x5B);

    // ----- The title screen

    private static Scene TitleScreen()
    {
        var scene = new Scene();
        scene.Add(Camera());

        var backdrop = new Entity("Backdrop");
        backdrop.Transform.Position = new Vector3(0, 0, -1);
        var sky = backdrop.AddComponent<Sprite>();
        (sky.Image, sky.Size, sky.Order) = (VisualNovelArt.Roof, ScreenSize, -10);
        scene.Add(backdrop);

        // Comet, twinkling in the gap where it belongs.
        var comet = new Entity("Comet");
        comet.Transform.Position = new Vector3(5.2f, 2.6f, 0);
        var star = comet.AddComponent<Sprite>();
        (star.Image, star.Size, star.Columns, star.Order) = (VisualNovelArt.CometSheet, new Vector2(2.2f), VisualNovelArt.CometFrames, 5);
        var twinkle = comet.AddComponent<Animation>();
        (twinkle.Clips, twinkle.Clip) = (VisualNovelArt.CometClips, "Happy");
        scene.Add(comet);

        scene.Add(Label("Title", new Vector2(0, 3.1f), "Starfall Café", 1.3f, Gold));
        scene.Add(Label("Subtitle", new Vector2(0, 2.05f), "A little star, a lot of flour, and one night to get it home.", 0.4f, Light));

        (string Label, ButtonAction Action, Key Shortcut, Color Color)[] buttons =
        [
            ("New Game", ButtonAction.NewGame, Key.Enter, Plum),
            ("Continue", ButtonAction.Continue, Key.None, Plum),
            ("Load", ButtonAction.OpenPanel, Key.None, Gray),
            ("Settings", ButtonAction.OpenPanel, Key.None, Gray),
            ("Quit", ButtonAction.Quit, Key.Escape, Gray),
        ];
        for (var i = 0; i < buttons.Length; i++)
        {
            var (label, action, shortcut, color) = buttons[i];
            var entity = MenuButton(label, new Vector2(0, 0.75f - i * 1.05f), action, action == ButtonAction.NewGame ? StoryScene : null, shortcut, color);
            entity.GetComponent<Sprite>()!.Size = new Vector2(4.2f, 0.85f);
            var button = entity.GetComponent<Button>()!;
            button.Size = new Vector2(4.2f, 0.85f);
            button.Panel = label switch { "Load" => ReadingMenus.LoadScreen, "Settings" => ReadingMenus.Settings, _ => null };
            entity.GetComponent<Text>()!.Size = 0.45f;
            scene.Add(entity);
        }

        var hint = Label("Hint", Vector2.Zero, "Click, tap or arrows and Enter  ·  Esc to quit", 0.32f, Dim);
        var anchor = hint.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.Bottom, new Vector2(0, 0.4f));
        scene.Add(hint);

        // The load screen and the settings, as the story has them.
        var menus = new Entity("Title Menus");
        scene.Add(menus);
        foreach (var (part, parent) in ReadingMenus.TitleParts())
            scene.Add(part, parent ?? menus);
        return scene;
    }

    // ----- The story

    private static Scene Story()
    {
        var scene = new Scene();
        scene.Add(Camera());

        var holder = new Entity(StageName);
        var stage = holder.AddComponent<Stage>();
        stage.Backgrounds = [$"Cafe: {VisualNovelArt.Cafe}", $"Roof: {VisualNovelArt.Roof}", "Dawn: #4A3F7A #F6A86E"];
        stage.Background = "Cafe";
        stage.Positions = [.. Stage.DefaultPositions, "Sky: 0, 3.3"];
        stage.Size = ScreenSize;
        stage.TransitionTime = 0.6f;
        stage.MoveTime = 0.6f;
        var dialogue = holder.AddComponent<Dialogue>();
        (dialogue.Start, dialogue.PauseGame, dialogue.Lines, dialogue.Layers) = (DialogueStart.SceneStart, false, Script, null);
        scene.Add(holder);

        scene.Add(Person("June", VisualNovelArt.JuneFaces, "Center"), holder);
        scene.Add(Person("Felix", VisualNovelArt.FelixFaces, "Center"), holder);
        scene.Add(Comet(), holder);

        var (box, tag, line) = StageNames.MakeBox();
        dialogue.Box = box.Name;
        line.GetComponent<Typewriter>()!.SoundEvery = 3;
        scene.Add(box);
        scene.Add(tag, box);
        scene.Add(line, box);

        // What happens off stage: the crash in the flour, the shop bell, Comet's glow coming back.
        var sounds = new Entity("Sound Effects");
        scene.Add(sounds);
        foreach (var (name, message, sound, volume) in new[]
                 {
                     ("Crash", "Crash", TemplateSounds.Explode, 0.7f), ("Shop Bell", "Bell", TemplateSounds.Coin, 0.8f),
                     ("Twinkle", "Twinkle", TemplateSounds.PowerUp, 0.8f),
                 })
        {
            var effect = new Entity(name);
            var player = effect.AddComponent<SoundPlayer>();
            (player.Message, player.Sound, player.Volume) = (message, sound, volume);
            scene.Add(effect, sounds);
        }

        ReadingMenus.AddTo(scene);
        foreach (var (panel, title, blurb) in Endings)
            AddEnding(scene, panel, title, blurb, Array.FindIndex(Endings, e => e.Panel == panel) + 1);
        AddPauseMenu(scene);
        return scene;
    }

    private static Entity Camera()
    {
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        (view.Size, view.Background, view.Transition) = (5, Night, SceneTransition.Fade);
        return camera;
    }

    // As much as a 16:9 screen shows of a camera 10 high.
    private static Vector2 ScreenSize => new(10 * 16 / 9f, 10);

    // Someone off stage until the story brings them on, a picture for each face.
    private static Entity Person(string name, IReadOnlyList<string> faces, string position)
    {
        var entity = new Entity(name);
        entity.AddComponent<Speaker>().Name = name;
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Image, sprite.Size) = (VisualNovelArt.Face(name, "Neutral"), new Vector2(3.2f, 5.76f));
        var character = entity.AddComponent<Character>();
        character.Expressions = [.. faces.Select(f => $"{f}: {VisualNovelArt.Face(name, f)}")];
        (character.Expression, character.Position) = ("Neutral", position);
        entity.IsEnabled = false;
        return entity;
    }

    // Comet: a sprite sheet whose faces are clips its animation plays, each twinkling.
    private static Entity Comet()
    {
        var entity = new Entity("Comet");
        entity.AddComponent<Speaker>().Name = "Comet";
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Columns) = (VisualNovelArt.CometSheet, new Vector2(2.4f), VisualNovelArt.CometFrames);
        var twinkle = entity.AddComponent<Animation>();
        (twinkle.Clips, twinkle.Clip) = (VisualNovelArt.CometClips, "Neutral");
        var character = entity.AddComponent<Character>();
        character.Expressions = [.. VisualNovelArt.CometFaces.Select(f => $"{f}: {f}")];
        (character.Expression, character.Position) = ("Neutral", "Right");
        entity.IsEnabled = false;
        return entity;
    }

    // An ending: opened by its message as the story's last line is read on, with its name, what happened, and the way back
    // to the title or round again.
    private static void AddEnding(Scene scene, string name, string title, string blurb, int number)
    {
        var panel = new Entity(name) { IsEnabled = false };
        panel.Transform.Position = new Vector3(0, 0, 2);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var end = panel.AddComponent<Panel>();
        (end.OpenMessage, end.CloseOnBack) = (name.Replace(" ", "", StringComparison.Ordinal), false);
        var backdrop = panel.AddComponent<Sprite>();
        (backdrop.Size, backdrop.Tint, backdrop.Order) = (new Vector2(12, 6.4f), new Color(0.05f, 0.06f, 0.16f, 1), 400);
        scene.Add(panel);

        foreach (var (label, at, value, size, color) in new[]
                 {
                     ("The End", 2.4f, "THE END", 0.4f, Dim),
                     ("Ending Title", 1.45f, title, 0.85f, Gold),
                     ("Blurb", 0.35f, blurb, 0.36f, Light),
                     ("Which", -0.45f, $"Ending {number} of {Endings.Length}", 0.32f, Dim),
                 })
        {
            var text = Label(label, new Vector2(0, at), value, size, color);
            text.GetComponent<Text>()!.Order = 401;
            scene.Add(text, panel);
        }

        scene.Add(MenuButton("Play Again", new Vector2(-2.3f, -1.95f), ButtonAction.NewGame, StoryScene, Key.Enter, Plum, order: 410), panel);
        scene.Add(MenuButton("Title", new Vector2(2.3f, -1.95f), ButtonAction.LoadScene, Project.DefaultSceneName, Key.Escape, Gray, order: 410), panel);
    }

    // The pause menu, for the keys and a controller: Esc, P or Start opens it, and from it the save and load screens, the
    // settings and the title screen.
    private static void AddPauseMenu(Scene scene)
    {
        var panel = new Entity("Paused") { IsEnabled = false };
        panel.Transform.Position = new Vector3(0, 0, 3);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        panel.AddComponent<Panel>().OpenOn = "Pause";
        var backdrop = panel.AddComponent<Sprite>();
        (backdrop.Size, backdrop.Tint, backdrop.Order) = (new Vector2(7, 8.4f), new Color(0.06f, 0.07f, 0.14f, 0.94f), 300);
        scene.Add(panel);

        var heading = Label("Heading", new Vector2(0, 3.3f), "Paused", 0.9f, Gold);
        heading.GetComponent<Text>()!.Order = 301;
        scene.Add(heading, panel);
        (string Label, ButtonAction Action, string? Target)[] buttons =
        [
            ("Resume", ButtonAction.ClosePanel, null),
            ("Save", ButtonAction.OpenPanel, ReadingMenus.SaveScreen),
            ("Load", ButtonAction.OpenPanel, ReadingMenus.LoadScreen),
            ("Settings", ButtonAction.OpenPanel, ReadingMenus.Settings),
            ("Title", ButtonAction.LoadScene, Project.DefaultSceneName),
        ];
        for (var i = 0; i < buttons.Length; i++)
        {
            var (label, action, target) = buttons[i];
            var entity = MenuButton(label, new Vector2(0, 2.1f - i * 1.15f), action, action == ButtonAction.LoadScene ? target : null, Key.None,
                i == 0 ? Plum : Gray, order: 310);
            entity.GetComponent<Button>()!.Panel = action == ButtonAction.OpenPanel ? target : null;
            scene.Add(entity, panel);
        }

        // "{Pause}" shows Esc on the keyboard and Start on a gamepad.
        var hint = Label("Hint", new Vector2(0, -3.75f), "{Pause} to resume", 0.32f, Dim);
        hint.GetComponent<Text>()!.Order = 301;
        scene.Add(hint, panel);
    }
}
