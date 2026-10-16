using System.Numerics;
using Griff.Audio;
using Griff.Input;
using Griff.Scenes;
using Griff.Templates;
using static Griff.Templates.ProjectTemplates;

namespace Griff.Modules.Adventures;

// The Adventure template, "Where's Pip?": a short point-and-click adventure with no code. Pip the cat is stuck up Mabel's
// apple tree; the hero takes a mug from the kitchen and makes tea with it, gives the tea to Mabel over the fence for the
// shed key she's been keeping, unlocks the shed, borrows Grandad's fishing rod, hooks a sardine from the kitchen cupboard
// on it and dangles it up to Pip, who comes down and goes home: the ending. Three rooms joined by hotspots that go to the
// next scene, a walker with an inventory bar (kept in the save data), hotspots that say lines, give items, set flags and
// talk, Use Withs for the items, a combination, Mabel's dialogue with choices, and a trigger that ends it.
internal sealed class AdventureTemplate : ProjectTemplate
{
    public override string Description => "Where's Pip? A short point-and-click adventure across a kitchen, a garden and a shed: walk where you click, look at and use things, talk to Mabel next door, and find, use and put together items to get Pip the cat down from the apple tree. Click to walk and use, right-click to look; or arrows, Space to use, X to look and E for your items.";

    public override int Order => 128;

    protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Chill, seed: 12) with { Volume = 0.5f };

    protected override Scene BuildStartScene() => Menu();

    protected override Dictionary<string, Scene> BuildMoreScenes() => new()
    {
        ["Scenes/Kitchen.scene"] = Kitchen(),
        ["Scenes/Garden.scene"] = Garden(),
        ["Scenes/Shed.scene"] = Shed(),
        ["Scenes/Ending.scene"] = Ending(),
    };

    public override IReadOnlyDictionary<string, byte[]> Images() => AdventureArt.Files();

    // Items come in with a coin's chime, are picked up with a blip and put together with a power-up; what's used on
    // something clicks like a switch unless it says otherwise.
    protected override void WireSounds(Entity entity)
    {
        base.WireSounds(entity);
        if (entity.GetComponent<InventoryBar>() is { } bar)
        {
            TemplateSounds.Set(bar.GetSound, s => bar.GetSound = s, TemplateSounds.Coin);
            TemplateSounds.Set(bar.PickSound, s => bar.PickSound = s, TemplateSounds.Select);
            TemplateSounds.Set(bar.CombineSound, s => bar.CombineSound = s, TemplateSounds.PowerUp);
        }

        foreach (var use in entity.GetComponents<UseWith>())
            TemplateSounds.Set(use.Sound, s => use.Sound = s, TemplateSounds.Switch);
    }

    /// <summary>The flag set as the story starts (the first thing the hero says), which Start Over needs to be there.</summary>
    public const string StartedFlag = "Started";

    /// <summary>The flag set once Pip is home: he's in the kitchen from then on, and gone from the tree.</summary>
    public const string HomeFlag = "PipHome";

    /// <summary>What the walker's inventory is kept under in the save data.</summary>
    public const string ItemsKey = "Items";

    /// <summary>What the room the walker is in is kept under in the save data, for the menu's Play to go back to.</summary>
    public const string RoomKey = "Room";

    private const string Player = "Player";
    private const string Box = "Dialogue Box";

    private static readonly Color Gold = Color.FromBytes(0xF2, 0xC9, 0x4C);
    private static readonly Color Light = Color.FromBytes(0xF4, 0xEE, 0xE0);
    private static readonly Color Dim = Color.FromBytes(0xC8, 0xC0, 0xB0);
    private static readonly Color Green = Color.FromBytes(0x4E, 0x9A, 0x5A);
    private static readonly Color Gray = Color.FromBytes(0x4A, 0x46, 0x50);
    private static readonly Color Night = Color.FromBytes(0x1E, 0x1A, 0x22);
    private static readonly Color Shade = new(0.08f, 0.07f, 0.1f, 0.82f);

    // ----- The menu -----

    private static Scene Menu()
    {
        var scene = new Scene();
        AddCamera(scene);
        AddBackdrop(scene, AdventureArt.Garden);
        var pip = new Entity("Pip");
        pip.Transform.Position = new Vector3(-0.9f, 3.1f, 0);
        var cat = pip.AddComponent<Sprite>();
        (cat.Image, cat.Size, cat.Order) = (AdventureArt.Pip, new Vector2(1.25f, 1), 6);
        scene.Add(pip);

        var panel = new Entity("Panel");
        panel.Transform.Position = new Vector3(0, -0.75f, 0);
        var back = panel.AddComponent<Sprite>();
        (back.Size, back.Tint, back.Order) = (new Vector2(8.6f, 6.3f), Shade, 50);
        scene.Add(panel);

        scene.Add(Label("Title", new Vector2(0, 1.6f), "WHERE'S PIP?", 1.2f, Gold));
        scene.Add(Label("Subtitle", new Vector2(0, 0.7f), "A little point-and-click adventure", 0.42f, Dim));
        var play = MenuButton("Play", new Vector2(0, -0.55f), ButtonAction.LoadScene, "Kitchen", Key.Enter, Green);
        (play.GetComponent<Button>()!.Fade, play.GetComponent<Button>()!.SavedScene) = (0.5f, RoomKey);
        scene.Add(play);
        // Play carries on in the room left; starting over forgets the room, the flags and the items, once there are any.
        var again = MenuButton("Start Over", new Vector2(0, -1.8f), ButtonAction.NewGame, "Kitchen", Key.None, Gray);
        (again.GetComponent<Button>()!.NeedsSaved, again.GetComponent<Button>()!.Fade) = (StartedFlag, 0.5f);
        scene.Add(again);
        scene.Add(MenuButton("Quit", new Vector2(0, -3.05f), ButtonAction.Quit, null, Key.Escape, Gray));

        var hint = Label("Hint", Vector2.Zero, "Play carries on where you left off  ·  Arrows to choose, Enter to play", 0.34f, Dim);
        var anchor = hint.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.Bottom, new Vector2(0, 0.45f));
        scene.Add(hint);
        return scene;
    }

    // ----- The kitchen: where it starts -----

    private static Scene Kitchen()
    {
        var scene = Room(AdventureArt.Kitchen, "-8.4 -4.5; 8.4 -4.5; 8.4 -1.25; -8.4 -1.25", farScale: 1);

        // The table in the middle of the floor: walked round, its legs a hole in the floor.
        var table = new Entity("Table");
        table.Transform.Position = new Vector3(0.55f, -2.6f, 0);
        var top = table.AddComponent<Sprite>();
        (top.Image, top.Size, top.Order) = (AdventureArt.Table, new Vector2(4, 2.25f), 5);
        var legs = table.AddComponent<WalkArea>();
        (legs.Points, legs.Blocks) = ("-2.05 -1.25; 2.05 -1.25; 2.05 -0.55; -2.05 -0.55", true);
        scene.Add(table);

        var player = AddPlayer(scene, new Vector2(-2.6f, -2.1f));
        // The first time: why we're here. Once said, never again (its flag is kept).
        var intro = player.AddComponent<Dialogue>();
        (intro.Start, intro.Box, intro.Layers) = (DialogueStart.SceneStart, Box, null);
        intro.Lines =
        [
            $"[if {StartedFlag}] -> End",
            $"[set {StartedFlag}] Pip? Here, Pip! Breakfast!",
            "...Nothing. He's been out all night, and he never misses breakfast.",
            "I'd better go and find him. Click to walk, right-click to look at things.",
        ];

        scene.Add(Spot("Kettle", new Vector2(-6.2f, 1.7f), "-0.6 -0.5; 0.55 -0.5; 0.55 0.55; -0.6 0.55", new Vector2(0.4f, -3.2f), h =>
        {
            (h.LookLine, h.UseLine) = ("The kettle's just boiled. Tea would hit the spot.", "I need a cup to pour it into.");
            h.WrongItemLine = "I'm not putting that in the kettle.";
        }, uses: [("Cup", u => (u.Line, u.Gives, u.Sound) = ("One nice hot cup of tea, coming up.", "Tea", TemplateSounds.Place))]));

        var cup = Spot("Cup", new Vector2(-4.1f, 1), null, new Vector2(0.3f, -2.5f), h =>
        {
            (h.LookLine, h.UseLine) = ("My favourite mug. It says WORLD'S OKAYEST GARDENER.", "I'll take my mug.");
            (h.UseGives, h.UseFlag, h.NeedsFlag, h.HideWhenGone) = ("Cup", "TookCup", "not TookCup", true);
        });
        ItemSprite(cup, 0);
        scene.Add(cup);

        // The wall cupboard: a tin of sardines the first time, and nothing much after.
        const string cupboard = "-1.4 -1.05; 1.4 -1.05; 1.4 1.05; -1.4 1.05";
        scene.Add(Spot("Cupboard", new Vector2(-3.5f, 2.7f), cupboard, new Vector2(0.2f, -4.2f), h =>
        {
            (h.LookLine, h.UseLine) = ("The cupboard. Where the good stuff lives.", "A tin of sardines! Pip's favourite.");
            (h.UseGives, h.UseFlag, h.NeedsFlag) = ("Sardines", "TookSardines", "not TookSardines");
        }));
        scene.Add(Spot("Empty Cupboard", new Vector2(-3.5f, 2.7f), cupboard, new Vector2(0.2f, -4.2f), h =>
        {
            (h.Name, h.NeedsFlag) = ("Cupboard", "TookSardines");
            (h.LookLine, h.UseLine) = ("Nothing left but a jar of pickled onions.", "Pickled onions. Pip won't come down for those.");
        }));

        scene.Add(Spot("Window", new Vector2(0, 2.2f), "-1.6 -1.4; 1.6 -1.4; 1.6 1.4; -1.6 1.4", new Vector2(0, -3.7f),
            h => h.LookLine = "The garden. I can't see Pip from here, but I can hear him meowing."));
        scene.Add(Spot("Photo", new Vector2(3.25f, 2.5f), "-0.8 -0.7; 0.8 -0.7; 0.8 0.7; -0.8 0.7", new Vector2(0, -4),
            h => (h.Name, h.LookLine, h.UseLine) = ("Pip's Photo", "Pip. Ginger, greedy and brave. Mostly greedy.", "I give the photo a little kiss. Don't tell anyone.")));
        scene.Add(Spot("Bowl", new Vector2(3.6f, -1.6f), "-0.6 -0.3; 0.6 -0.3; 0.6 0.3; -0.6 0.3", new Vector2(-1, -0.3f), h =>
        {
            (h.Name, h.LookLine) = ("Pip's Bowl", "Pip's bowl. He hasn't touched his breakfast. That's not like him.");
            h.WrongItemLine = "Pip's bowl is for Pip's things.";
        }, uses: [("Sardines", u => (u.Line, u.KeepsItem) = ("He isn't here to eat them. He's out there somewhere.", true))]));
        scene.Add(Spot("Back Door", new Vector2(6.375f, 1), "-1.2 -2.1; 1.2 -2.1; 1.2 2.1; -1.2 2.1", new Vector2(0, -2.5f), h =>
        {
            h.LookLine = "The back door, out to the garden.";
            (h.GoesTo, h.ArriveAt) = ("Garden", "Back Door");
        }));

        // Home at last, once it's over.
        var home = Spot("Pip", new Vector2(-4.6f, -1.1f), null, new Vector2(1.1f, -0.4f), h =>
        {
            (h.LookLine, h.UseLine) = ("Pip, home at last, licking sardine off his whiskers.", "Purrrrrr.");
            (h.NeedsFlag, h.HideWhenGone) = (HomeFlag, true);
        });
        var curled = home.AddComponent<Sprite>();
        (curled.Image, curled.Size, curled.Order) = (AdventureArt.Pip, new Vector2(1.25f, 1), 5);
        scene.Add(home);

        AddDialogueBox(scene);
        AddHud(scene);
        return scene;
    }

    // ----- The garden: Pip up the tree, Mabel over the fence, the shed -----

    private static Scene Garden()
    {
        var scene = Room(AdventureArt.Garden, "-8.4 -4.5; 8.4 -4.5; 8.4 -1.2; -8.4 -1.2", farScale: 0.85f);
        AddPlayer(scene, new Vector2(-5, -2));

        scene.Add(Spot("Back Door", new Vector2(-6.75f, 0.85f), "-1.1 -1.95; 1.1 -1.95; 1.1 1.95; -1.1 1.95", new Vector2(0.4f, -2.35f), h =>
        {
            h.LookLine = "Back into the kitchen.";
            (h.GoesTo, h.ArriveAt) = ("Kitchen", "Back Door");
        }));
        scene.Add(Spot("Apple Tree", new Vector2(-3, 2.2f), "-0.4 -3.3; 0.4 -3.3; 0.4 -0.4; 2.2 -0.4; 2.4 2.8; -2.2 2.8; -2.2 -0.4; -0.4 -0.4",
            new Vector2(0.7f, -3.7f), h => (h.LookLine, h.UseLine) =
                ("Mabel's apple tree. Tall, old, and very climbable, if you're a cat.", "I'm not climbing that. I'm not a cat.")));

        // Pip, up on a branch: only something fishy on a line brings him down, and then it's over.
        var pip = Spot("Pip", new Vector2(-0.9f, 3.1f), null, new Vector2(-0.6f, -4.6f), h =>
        {
            (h.LookLine, h.UseLine) = ("Pip! He's stuck up the tree. Or he's pretending to be.", "Come down, Pip! ...He just looks at me.");
            (h.NeedsFlag, h.HideWhenGone) = ($"not {HomeFlag}", true);
            h.WrongItemLine = "Pip sniffs at it from up there, unimpressed.";
        }, uses:
        [
            ("Sardines", u => (u.Line, u.KeepsItem) = ("I wave the tin at him. He's interested, but he can't reach it up there.", true)),
            ("Fishing Rod", u => (u.Line, u.KeepsItem) = ("I dangle the line up to him. He's not falling for an empty hook.", true)),
            ("Fishy Rod", u => (u.Talks, u.Sound) = (true, TemplateSounds.PowerUp)),
        ]);
        var cat = pip.AddComponent<Sprite>();
        (cat.Image, cat.Size, cat.Order) = (AdventureArt.Pip, new Vector2(1.25f, 1), 6);
        pip.AddComponent<Speaker>().PortraitImage = AdventureArt.PipFace;
        var coming = pip.AddComponent<Dialogue>();
        (coming.Box, coming.Message, coming.Layers) = (Box, "TheEnd", null);
        coming.Lines =
        [
            "You: Pip... look what I've got for you...",
            "Mrrrow?",
            "MRROW!",
            "You: He's coming down! Easy... easy... Got you!",
            "You: Come on, you rascal. Let's go home.",
        ];
        scene.Add(pip);

        // Once he's down: home, and the end.
        var homeward = new Entity("Pip Goes Home");
        var home = homeward.AddComponent<Trigger>();
        (home.Message, home.Do, home.With) = ("TheEnd", "Set Flag", HomeFlag);
        scene.Add(homeward);
        var ending = new Entity("The End");
        var end = ending.AddComponent<Trigger>();
        (end.Message, end.Do, end.With) = ("TheEnd", "Load Scene", "Ending");
        scene.Add(ending);

        // Mabel over the fence: a chat with choices, and the shed key for a cup of tea.
        var mabel = Spot("Mabel", new Vector2(2.25f, 1.375f), null, new Vector2(0, -2.875f), h =>
        {
            (h.LookLine, h.UseTalks) = ("Mabel from next door. She knows everything that goes on round here.", true);
            h.WrongItemLine = "Mabel's too polite to say no, but she doesn't want that.";
        }, uses:
        [
            ("Tea", u => (u.Flag, u.Gives, u.Talks) = ("GaveTea", "Shed Key", true)),
            ("Sardines", u => (u.Line, u.KeepsItem) = ("She says she's more of a kipper person.", true)),
        ]);
        var lady = mabel.AddComponent<Sprite>();
        (lady.Image, lady.Size, lady.Order) = (AdventureArt.Mabel, new Vector2(1.5f, 1.75f), 4);
        mabel.AddComponent<Speaker>().PortraitImage = AdventureArt.MabelFace;
        var chat = mabel.AddComponent<Dialogue>();
        (chat.Box, chat.Layers) = (Box, null);
        chat.Lines =
        [
            "[if GaveTea] [if not Thanked] -> Tea",
            "[if MetMabel] Back again, dearie?",
            "[if not MetMabel] [set MetMabel] Morning, dearie! Lovely day for it.",
            "-> Ask",
            "# Ask",
            "> Have you seen Pip? -> Pip",
            "> [if not Thanked] Can I have my shed key back? -> Key",
            "> [if Thanked] Thanks for the key! -> Thanks",
            "> Bye, Mabel. -> Bye",
            "# Pip",
            "That rascal? He's been up my apple tree since sunrise.",
            "You: He won't come down for me.",
            "Cats never come down for people, dearie. They come down for fish.",
            "-> Ask",
            "# Key",
            "Your shed key? I've had it since you went on holiday.",
            "I'll give it back after a nice cup of tea. My kettle's on the blink.",
            "-> Ask",
            "# Thanks",
            "Mind the spiders in that shed. Big as saucers, they are.",
            "-> Ask",
            "# Tea",
            "[set Thanked] Ooh, a cuppa! You're a treasure.",
            "Here's your shed key back, dearie. Don't lose it.",
            "-> End",
            "# Bye",
            "Ta-ra, dearie!",
        ];
        scene.Add(mabel);

        var gnome = Spot("Gnome", new Vector2(4, -1), null, new Vector2(-1, -0.6f), h =>
            (h.LookLine, h.UseLine) = ("A garden gnome. He's seen things.", "He's fishing too. No luck either, by the look of him."));
        var hat = gnome.AddComponent<Sprite>();
        (hat.Image, hat.Size, hat.Order) = (AdventureArt.Gnome, new Vector2(0.75f, 1), 5);
        scene.Add(gnome);

        // The shed: locked until the key's used on it, then a way in.
        scene.Add(Spot("Shed Door", new Vector2(6.25f, 0.45f), "-0.95 -1.6; 0.95 -1.6; 0.95 1.6; -0.95 1.6", new Vector2(0, -1.95f), h =>
        {
            (h.LookLine, h.UseLine) = ("Grandad's old shed. Locked tight with a padlock.", "It's locked. Mabel has the key, I think.");
            h.NeedsFlag = "not ShedOpen";
        }, uses: [("Shed Key", u => (u.Line, u.Flag) = ("The padlock springs open with a clunk.", "ShedOpen"))]));
        var shed = Spot("Shed", new Vector2(6.25f, 0.375f), null, new Vector2(0, -1.875f), h =>
        {
            h.LookLine = "The shed. It smells of oil and old tomatoes.";
            (h.NeedsFlag, h.HideWhenGone, h.GoesTo, h.ArriveAt) = ("ShedOpen", true, "Shed", "Doorway");
        });
        var doorway = shed.AddComponent<Sprite>();
        (doorway.Image, doorway.Size, doorway.Order) = (AdventureArt.ShedOpen, new Vector2(1.5f, 3), -5);
        scene.Add(shed);

        AddDialogueBox(scene);
        AddHud(scene);
        return scene;
    }

    // ----- The shed: the fishing rod -----

    private static Scene Shed()
    {
        var scene = Room(AdventureArt.Shed, "-8.2 -4.5; 8.4 -4.5; 8.4 -1.3; -8.2 -1.3", farScale: 0.95f);
        AddPlayer(scene, new Vector2(-5.5f, -2.2f));

        scene.Add(Spot("Doorway", new Vector2(-7.5f, 0.8f), "-1.1 -2; 1.1 -2; 1.1 2; -1.1 2", new Vector2(0.6f, -2.3f), h =>
        {
            (h.Name, h.LookLine) = ("Garden", "Back out to the garden.");
            (h.GoesTo, h.ArriveAt) = ("Garden", "Shed");
        }));
        var rod = Spot("Fishing Rod", new Vector2(6.6f, 0.25f), null, new Vector2(-0.8f, -1.75f), h =>
        {
            (h.LookLine, h.UseLine) = ("Grandad's old fishing rod. He never caught a thing with it.", "I'll borrow this. Sorry, Grandad.");
            (h.UseGives, h.UseFlag, h.NeedsFlag, h.HideWhenGone) = ("Fishing Rod", "TookRod", "not TookRod", true);
        });
        var cane = rod.AddComponent<Sprite>();
        (cane.Image, cane.Size, cane.Order) = (AdventureArt.Rod, new Vector2(0.75f, 3), 5);
        scene.Add(rod);

        scene.Add(Spot("Workbench", new Vector2(-2.25f, 0.4f), "-3.1 -1.6; 3.1 -1.6; 3.1 2.85; -3.1 2.85", new Vector2(0, -1.9f),
            h => (h.LookLine, h.UseLine) = ("Grandad's workbench. Every jar on it is labelled MISC.", "I could fix something. But Pip comes first.")));
        scene.Add(Spot("Lawnmower", new Vector2(3, 0.2f), "-1.4 -1.5; 1.8 -1.5; 1.8 1.4; -1.4 1.4", new Vector2(0, -1.7f),
            h => (h.LookLine, h.UseLine) = ("The lawnmower. Pip is scared of it. So am I.", "Mowing the lawn can wait.")));
        scene.Add(Spot("Window", new Vector2(2.1f, 2.6f), "-1.4 -0.95; 1.4 -0.95; 1.4 1; -1.4 1", new Vector2(0, -4.1f),
            h => (h.Name, h.LookLine) = ("Dusty Window", "So dusty. I can just make out Mabel's roses through it.")));
        scene.Add(Spot("Paint Tins", new Vector2(6, 2.6f), "-1.9 -0.5; 1.9 -0.5; 1.9 0.5; -1.9 0.5", new Vector2(-0.5f, -4.1f),
            h => h.LookLine = "Paint, in every colour Grandad ever liked. Mostly brown."));
        scene.Add(Spot("Spider", new Vector2(8, 4), "-1.3 -0.9; 0.9 -0.9; 0.9 1; -1.3 1", Vector2.Zero, h =>
        {
            (h.LookLine, h.UseLine, h.WalkThere) = ("A spider, big as a saucer. Mabel wasn't joking.", "I'll leave him be. He was here first.", false);
            h.WrongItemLine = "I'm not getting that close to him.";
        }));

        AddDialogueBox(scene);
        AddHud(scene);
        return scene;
    }

    // ----- The ending -----

    private static Scene Ending()
    {
        var scene = new Scene();
        AddCamera(scene);
        AddBackdrop(scene, AdventureArt.Kitchen);
        var table = new Entity("Table");
        table.Transform.Position = new Vector3(0.55f, -2.6f, 0);
        var top = table.AddComponent<Sprite>();
        (top.Image, top.Size, top.Order) = (AdventureArt.Table, new Vector2(4, 2.25f), 5);
        scene.Add(table);

        var hero = new Entity("Hero");
        hero.Transform.Position = new Vector3(1.4f, -0.4f, 0);
        var figure = hero.AddComponent<Sprite>();
        (figure.Image, figure.Size, figure.Columns, figure.Order) = (AdventureArt.Hero, new Vector2(1.5f, 2.5f), AdventureArt.HeroFrames, 5);
        scene.Add(hero);
        var pip = new Entity("Pip");
        pip.Transform.Position = new Vector3(3, -1.15f, 0);
        var cat = pip.AddComponent<Sprite>();
        (cat.Image, cat.Size, cat.Order, cat.FlipX) = (AdventureArt.Pip, new Vector2(1.25f, 1), 6, true);
        scene.Add(pip);

        var panel = new Entity("Panel");
        panel.Transform.Position = new Vector3(0, 2.9f, 0);
        var back = panel.AddComponent<Sprite>();
        (back.Size, back.Tint, back.Order) = (new Vector2(12.5f, 3.4f), Shade, 50);
        scene.Add(panel);
        scene.Add(Label("Heading", new Vector2(0, 3.75f), "Home at Last", 1.1f, Gold));
        scene.Add(Label("Line", new Vector2(0, 2.65f), "Pip is home, full of sardines and very pleased with himself.", 0.42f, Light));
        scene.Add(Label("Thanks", new Vector2(0, 1.85f), "THE END  ·  Thanks for playing!", 0.38f, Dim));

        var again = MenuButton("Play Again", new Vector2(-2.4f, -3.9f), ButtonAction.NewGame, "Kitchen", Key.Enter, Green);
        again.GetComponent<Button>()!.Fade = 0.5f;
        scene.Add(again);
        scene.Add(MenuButton("Menu", new Vector2(2.4f, -3.9f), ButtonAction.LoadScene, "Main", Key.Escape, Gray));
        return scene;
    }

    // ----- What every room has -----

    // A room: the camera (sorting what stands on the floor by where it stands), its picture and its floor.
    private static Scene Room(string picture, string floor, float farScale)
    {
        var scene = new Scene();
        AddCamera(scene).SortByY = true;
        AddBackdrop(scene, picture);
        var area = new Entity("Floor");
        var walk = area.AddComponent<WalkArea>();
        (walk.Points, walk.FarScale) = (floor, farScale);
        scene.Add(area);
        return scene;
    }

    private static Camera AddCamera(Scene scene)
    {
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        (view.Size, view.Background, view.Transition) = (5, Night, SceneTransition.Fade);
        scene.Add(camera);
        return view;
    }

    // The room's picture, filling the view (18 by 10 units, a little wider than a 16:9 screen).
    private static void AddBackdrop(Scene scene, string picture)
    {
        var backdrop = new Entity("Backdrop");
        var sprite = backdrop.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (picture, new Vector2(18, 10), -10);
        scene.Add(backdrop);
    }

    // The hero, feet at `feet`: walks where it's clicked, says what it sees, carries what it finds (kept in the save
    // data, so it's there in the next room and the next time the game runs) in a bar along the bottom of the screen.
    private static Entity AddPlayer(Scene scene, Vector2 feet)
    {
        var player = new Entity(Player);
        player.Transform.Position = new Vector3(feet.X, feet.Y + 1.25f, 0);
        var sprite = player.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Columns, sprite.Order) = (AdventureArt.Hero, new Vector2(1.5f, 2.5f), AdventureArt.HeroFrames, 5);
        var animation = player.AddComponent<Animation>();
        (animation.Clips, animation.Clip) = ("Idle 0, Walk 1-4 @8", "Idle");
        var walker = player.AddComponent<Walker>();
        (walker.Speed, walker.SaveRoomAs) = (3.2f, RoomKey);
        var speaker = player.AddComponent<Speaker>();
        (speaker.Name, speaker.PortraitImage) = ("You", AdventureArt.HeroFace);
        player.AddComponent<Inventory>().SaveAs = ItemsKey;
        var bar = player.AddComponent<InventoryBar>();
        bar.Icons = [.. AdventureArt.ItemNames.Select((item, i) => $"{item}: {AdventureArt.Items} {AdventureArt.ItemNames.Length}x1 {i}")];
        bar.Descriptions =
        [
            "Cup: My favourite mug. It says WORLD'S OKAYEST GARDENER.",
            "Tea: A nice hot cup of tea. Milk, two sugars.",
            "Shed Key: Grandad's shed key. Rusty, but trusty.",
            "Fishing Rod: Grandad's old fishing rod. There's nothing on the hook.",
            "Sardines: A tin of sardines. Pip would do anything for one of these.",
            "Fishy Rod: A sardine dangling on a fishing line. Irresistible, if you're a cat.",
        ];
        bar.Combinations = ["Fishy Rod = Fishing Rod + Sardines: I hook a sardine on the line. Perfect cat bait."];
        bar.WrongLine = "I don't think that goes there.";
        scene.Add(player);
        return player;
    }

    // A hotspot called `name` at `at`: its shape (null for its sprite's), where the walker stands, and the rest set up by
    // `set`, with a Use With for each item that does something here.
    private static Entity Spot(string name, Vector2 at, string? points, Vector2 standAt, Action<Hotspot> set,
        (string Item, Action<UseWith> Set)[]? uses = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        var hotspot = entity.AddComponent<Hotspot>();
        (hotspot.Points, hotspot.StandAt) = (points, standAt);
        set(hotspot);
        foreach (var (item, setUse) in uses ?? [])
        {
            var use = entity.AddComponent<UseWith>();
            use.Item = item;
            setUse(use);
        }

        return entity;
    }

    // An item lying in a room, drawn with its icon from the items sheet.
    private static void ItemSprite(Entity entity, int frame)
    {
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Image, sprite.Columns, sprite.Frame, sprite.Size, sprite.Order) = (AdventureArt.Items, AdventureArt.ItemNames.Length, frame, Vector2.One, 5);
    }

    // The box what's said is shown in, along the top of the screen (the inventory bar has the bottom), with a portrait and
    // a name tag for whoever's talking. Hidden until someone talks.
    private static void AddDialogueBox(Scene scene)
    {
        var entity = new Entity(Box) { IsEnabled = false };
        entity.Transform.Position = new Vector3(0, 0, 2);
        var anchor = entity.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.Top, new Vector2(0, -1.8f));
        var panel = entity.AddComponent<Sprite>();
        (panel.Size, panel.Tint, panel.Order) = (new Vector2(15.4f, 2.1f), new Color(0.08f, 0.07f, 0.1f, 0.92f), 200);
        var text = entity.AddComponent<Text>();
        (text.Value, text.Size, text.Order) = ("...", 0.4f, 201);
        entity.AddComponent<Typewriter>();
        entity.AddComponent<DialogueBox>().WrapWidth = 11;
        scene.Add(entity);

        var portrait = new Entity("Portrait");
        portrait.Transform.Position = new Vector3(-6.6f, 0, 0);
        var picture = portrait.AddComponent<Sprite>();
        (picture.Size, picture.Order) = (new Vector2(1.7f, 1.7f), 201);
        scene.Add(portrait, entity);

        var tag = new Entity("Name Tag");
        tag.Transform.Position = new Vector3(-4.6f, -1.35f, 0);
        var backdrop = tag.AddComponent<Sprite>();
        (backdrop.Size, backdrop.Tint, backdrop.Order) = (new Vector2(2.6f, 0.62f), panel.Tint, 200);
        var name = tag.AddComponent<Text>();
        (name.Value, name.Size, name.Color, name.Order) = ("Name", 0.38f, Gold, 201);
        scene.Add(tag, entity);
    }

    // How to play in a corner, Look and Use buttons for a touch screen (which has no right click), and the pause menu.
    private static void AddHud(Scene scene)
    {
        // "{Interact}" shows E on the keyboard and Y on a gamepad.
        var help = Help("Click to walk and use  ·  right-click to look  ·  {Interact} for your items", Anchor.TopLeft);
        help.GetComponent<Text>()!.Color = Light;
        scene.Add(help);
        var backing = new Entity("Backing");
        backing.Transform.Position = new Vector3(3.6f, 0.02f, 0);
        var strip = backing.AddComponent<Sprite>();
        (strip.Size, strip.Tint, strip.Order) = (new Vector2(7.6f, 0.56f), new Color(0.08f, 0.07f, 0.1f, 0.6f), 199);
        scene.Add(backing, help);
        scene.Add(VerbButton("Look", "Choose Look", new Vector2(-2.3f, 0.55f)));
        scene.Add(VerbButton("Use", "Choose Use", new Vector2(-0.6f, 0.55f)));
        AddPauseMenu(scene, menuScene: "Main");
    }

    // A small button in the bottom right corner that switches what a click on a hotspot does.
    private static Entity VerbButton(string label, string action, Vector2 offset)
    {
        var size = new Vector2(1.5f, 0.6f);
        var entity = new Entity(label);
        var anchor = entity.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.BottomRight, offset);
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Size, sprite.Tint, sprite.Order) = (size, new Color(0.08f, 0.07f, 0.1f, 0.75f), 150);
        var text = entity.AddComponent<Text>();
        (text.Value, text.Size, text.Color, text.Order) = (label, 0.34f, Light, 151);
        var button = entity.AddComponent<Button>();
        (button.Size, button.Action, button.Do, button.Targets, button.Navigable) = (size, ButtonAction.Do, action, Player, false);
        return entity;
    }
}
