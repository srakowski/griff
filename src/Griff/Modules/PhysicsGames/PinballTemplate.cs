using System.Numerics;
using Griff.Audio;
using Griff.Input;
using Griff.Scenes;
using Griff.Templates;
using static Griff.Modules.PhysicsGames.PinballParts;
using static Griff.Templates.ProjectTemplates;

namespace Griff.Modules.PhysicsGames;

// The Pinball template, "Midnight Midway": a title screen with the best score, and one table at a night carnival to play
// three balls on. Flippers, pop bumpers and slingshot kickers, three top lanes (M, I, D) whose set raises the multiplier
// up to 5x, a bank of drop targets (twice down is an extra ball, once a game), an orbit up the left, a walled skyway up the
// right, a bell hanging on a rope, inlanes back to the flippers and outlanes to the drain, a ball save, a tilt, and the
// best score kept. All of it the pinball pieces and core no-code components: counters (one multiplied by another),
// triggers, sound players, spawners and panels.
internal sealed class PinballTemplate : ProjectTemplate
{
    public override string Description => "Midnight Midway, a pinball table at a night carnival: three balls to score with, lanes that raise the multiplier up to 5x, drop targets that earn an extra ball, an orbit, a skyway, a bell, a ball save, a tilt, and the best score kept. Hold Space (or the down arrow) and let go to shoot, Left and Right Shift (or Z and /) to flip, N to nudge; click or tap either side to flip; a controller's shoulders, A or the right stick, and B.";

    public override int Order => 86;

    protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Bright, seed: 41) with { Volume = 0.5f };

    protected override Scene BuildStartScene() => TitleScreen();

    protected override Dictionary<string, Scene> BuildMoreScenes() => new() { [$"Scenes/{TableScene}.scene"] = Table() };

    protected override Dictionary<string, Entity> BuildPrefabs() => new()
    {
        ["Prefabs/Ball Saved.prefab"] = Callout("Ball Saved", "BALL SAVED", Color.FromBytes(0x7C, 0xF0, 0x9A)),
        ["Prefabs/Extra Ball.prefab"] = Callout("Extra Ball", "EXTRA BALL!", Gold),
        ["Prefabs/New Best.prefab"] = Callout("New Best", "NEW BEST!", Pink),
    };

    public override IReadOnlyDictionary<string, byte[]> Images()
    {
        var files = PinballArt.Files();
        foreach (var (path, bytes) in MidwayArt.Files())
            files[path] = bytes;
        return files;
    }

    // Its own sounds: the multiplier going up, an extra ball, the bell, a new best and the game over.
    protected override IReadOnlyDictionary<string, SoundRecipe> MoreSounds => new Dictionary<string, SoundRecipe>
    {
        [MultiplierSound] = new() { Wave = Waveform.Square, SquareDuty = 0.35f, Frequency = 0.42f, ChangeAmount = 0.35f, ChangeSpeed = 0.62f, Sustain = 0.1f, Decay = 0.25f, Punch = 0.3f, Volume = 0.45f },
        [ExtraBallSound] = new() { Wave = Waveform.Square, SquareDuty = 0.25f, Frequency = 0.36f, Slide = 0.18f, RepeatSpeed = 0.62f, ChangeAmount = 0.25f, ChangeSpeed = 0.7f, Sustain = 0.3f, Decay = 0.35f, Punch = 0.3f, Volume = 0.5f },
        [BellSound] = new() { Wave = Waveform.Sine, Frequency = 0.62f, VibratoDepth = 0.12f, VibratoSpeed = 0.55f, Sustain = 0.05f, Decay = 0.6f, Punch = 0.5f, Volume = 0.55f },
        [NewBestSound] = new() { Wave = Waveform.Square, SquareDuty = 0.2f, Frequency = 0.48f, ChangeAmount = 0.45f, ChangeSpeed = 0.5f, RepeatSpeed = 0.55f, Sustain = 0.25f, Decay = 0.3f, Punch = 0.35f, Volume = 0.5f },
        [GameOverSound] = new() { Wave = Waveform.Triangle, Frequency = 0.4f, Slide = -0.12f, ChangeAmount = -0.3f, ChangeSpeed = 0.55f, VibratoDepth = 0.2f, VibratoSpeed = 0.5f, Sustain = 0.35f, Decay = 0.5f, Volume = 0.55f },
    };

    public const string MultiplierSound = "Sounds/multiplier.wav";
    public const string ExtraBallSound = "Sounds/extra-ball.wav";
    public const string BellSound = "Sounds/bell.wav";
    public const string NewBestSound = "Sounds/new-best.wav";
    public const string GameOverSound = "Sounds/game-over.wav";

    /// <summary>The scene the table is in.</summary>
    public const string TableScene = "Table";

    /// <summary>What the best score is kept under in the save data.</summary>
    public const string BestKey = "Pinball.Best";

    /// <summary>The most the multiplier goes up to.</summary>
    public const int MaxMultiplier = 5;

    /// <summary>The balls a game has, and the seconds of ball save after each is shot.</summary>
    public const int Balls = 3;

    public const float BallSave = 8;

    // The messages that tie it together.
    public const string LanesLit = "Lanes Lit";
    public const string TargetsDown = "Targets Down";
    public const string ExtraBall = "Extra Ball";
    public const string BallLost = "Ball Lost";
    public const string BallSaved = "Ball Saved";
    public const string NewBest = "New Best";
    public const string GameOver = "GameOver";

    private static readonly Color Night = Color.FromBytes(0x0A, 0x08, 0x1C);
    private static readonly Color Gold = Color.FromBytes(0xFF, 0xCC, 0x4A);
    private static readonly Color Pink = Color.FromBytes(0xFF, 0x6A, 0xC8);
    private static readonly Color Light = Color.FromBytes(0xF4, 0xF0, 0xFF);
    private static readonly Color Dim = Color.FromBytes(0xB0, 0xA8, 0xD8);
    private static readonly Color Panel = new(0.08f, 0.05f, 0.18f, 0.94f);
    private static readonly Color Violet = Color.FromBytes(0x6A, 0x3C, 0xC8);
    private static readonly Color Gray = Color.FromBytes(0x3A, 0x34, 0x58);

    // ----- The table

    private static Scene Table()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        (view.Size, view.Background, view.Transition) = (CameraSize, Night, SceneTransition.Fade);
        scene.Add(camera);

        var backdrop = new Entity("Midway");
        backdrop.Transform.Position = new Vector3(0, 0, -5);
        var picture = backdrop.AddComponent<Sprite>();
        (picture.Image, picture.Size, picture.Order) = (MidwayArt.Backdrop, new Vector2(40, 22), -40);
        scene.Add(backdrop);

        var table = new Entity("Table");
        var tilt = table.AddComponent<Tilt>();
        (tilt.Within, tilt.NudgeSound, tilt.WarningSound, tilt.TiltSound) = ("Table", PinballArt.NudgeSound, PinballArt.WarningSound, PinballArt.TiltSound);
        scene.Add(table);
        foreach (var piece in Pieces())
            scene.Add(piece, table);

        AddScoring(scene);
        AddHud(scene);
        scene.Add(GameOverPanel());
        AddPauseMenu(scene, menuScene: "Main");
        return scene;
    }

    // Where the multiplier's inserts and the shoot-again light are, over the flippers.
    private static readonly Vector2 InsertsAt = new(Middle, -4.75f);

    // Every piece of the table, inside it: its walls, flippers, kickers, bumpers, lanes, targets, orbit, skyway, bell, inserts,
    // plunger, ball and drain.
    private static IEnumerable<Entity> Pieces()
    {
        var field = new Entity("Playfield");
        field.Transform.Position = new Vector3(0, 0, -1);
        var picture = field.AddComponent<Sprite>();
        (picture.Image, picture.Size, picture.Order) = (MidwayArt.Playfield, new Vector2(10, 18), -20);
        yield return field;

        // The walls round it, the top corners, the shooter lane's divider, the posts between the top lanes, and each side's
        // outlane post and the inlane guide down to the flipper (the outlane, outside them, goes down to the drain).
        var walls = new Entity("Walls");
        foreach (var wall in new[]
                 {
                     Wall("Left Wall", new Vector2(Left - 0.2f, -9.6f), new Vector2(Left - 0.2f, 9.4f), 0.4f),
                     Wall("Right Wall", new Vector2(Right + 0.2f, -9.6f), new Vector2(Right + 0.2f, 9.4f), 0.4f),
                     Wall("Top Wall", new Vector2(Left - 0.4f, 9.2f), new Vector2(Right + 0.4f, 9.2f), 0.4f),
                     Wall("Top Left Corner", new Vector2(Left, 7.2f), new Vector2(Left + 1.8f, 9), 0.4f),
                     Wall("Top Right Corner", new Vector2(Right - 1.8f, 9), new Vector2(Right, 7.2f), 0.4f),
                     Post("Lane Divider", Divider, -9.6f, 5, 0.3f),
                     Post("Post", Middle - 1.1f, 6.9f, 7.9f, 0.2f),
                     Post("Post", Middle + 1.1f, 6.9f, 7.9f, 0.2f),
                     Post("Left Outlane Post", OutlanePost, -4.6f, -2.9f, 0.15f),
                     Post("Right Outlane Post", Mirror(OutlanePost), -4.6f, -2.9f, 0.15f),
                     Wall("Left Guide", new Vector2(OutlanePost, -4.6f), new Vector2(PivotX - 0.45f, PivotY + 0.4f), 0.25f),
                     Wall("Right Guide", new Vector2(Mirror(OutlanePost), -4.6f), new Vector2(Mirror(PivotX - 0.45f), PivotY + 0.4f), 0.25f),
                 })
            Put(walls, wall);
        yield return walls;

        yield return PinballParts.Flipper(FlipperSide.Left, new Vector2(PivotX, PivotY), pivot: true);
        yield return PinballParts.Flipper(FlipperSide.Right, new Vector2(Mirror(PivotX), PivotY), pivot: true);
        yield return Kicker(FlipperSide.Left, new Vector2(KickerX, -3));
        yield return Kicker(FlipperSide.Right, new Vector2(Mirror(KickerX), -3));

        var bumpers = new Entity("Bumpers");
        foreach (var (name, at) in new[] { ("Left Bumper", new Vector2(-1.25f, 5.5f)), ("Right Bumper", new Vector2(0.95f, 5.5f)), ("Bottom Bumper", new Vector2(-0.15f, 4)) })
        {
            var bumper = new Entity(name);
            MakeBumper(bumper, at);
            Put(bumpers, bumper);
        }

        yield return bumpers;

        // M, I and D: each lights as the ball rolls through, and the three lit raise the multiplier (Lanes Lit).
        var lanes = new Entity("Top Lanes");
        var letters = "MID";
        var index = 0;
        foreach (var lane in Lanes(new Vector2(Middle, 7.4f), 2.2f))
        {
            lane.Name = $"Lane {letters[index]}";
            (lane.GetComponent<Sprite>()!.Image, lane.GetComponent<Sprite>()!.Size) = (MidwayArt.Insert, new Vector2(0.6f));
            lane.GetComponent<Sprite>()!.Tint = Color.FromBytes(0x30, 0x9C, 0xE8);
            var letter = lane.AddComponent<Text>();
            (letter.Value, letter.Size, letter.Color, letter.Order) = (letters[index].ToString(), 0.36f, Light, 1);
            var rollover = lane.GetComponent<Lane>()!;
            (rollover.CompleteMessage, rollover.CompletePoints) = (LanesLit, 1000);
            Put(lanes, lane);
            index++;
        }

        yield return lanes;

        // A bank of three drop targets up the left: each down scores 250, all three 1,000 more (Targets Down).
        var targets = new Entity("Drop Targets");
        foreach (var target in DropTargets(new Vector2(Left + 0.25f, 1.1f), upright: true))
        {
            var dropping = target.GetComponent<Target>()!;
            (dropping.CompleteMessage, dropping.CompletePoints) = (TargetsDown, 1000);
            Put(targets, target);
        }

        yield return targets;

        // The orbit: a channel up the left wall, round the top; through it scores 250.
        var orbit = new Entity("Orbit");
        Put(orbit, Post("Orbit Wall", -3.95f, 2.5f, 6, 0.2f));
        Put(orbit, ChannelLane("Orbit Lane", new Vector2((Left - 3.85f) / 2, 4.3f), 250));
        yield return orbit;

        // The skyway: up the right beside the shooter lane, a ramp into it from below; through it scores 500.
        var skyway = new Entity("Skyway");
        Put(skyway, Post("Skyway Wall", SkywayWall, 0.4f, 5.4f, 0.2f));
        Put(skyway, Wall("Skyway Ramp", new Vector2(2.5f, -1.05f), new Vector2(Divider - 0.15f, -0.3f), 0.15f));
        Put(skyway, ChannelLane("Skyway Lane", new Vector2((SkywayWall + 0.1f + Divider - 0.15f) / 2, 2.6f), 500));
        yield return skyway;

        // The bell, hanging on a rope from the top wall: it swings as it's hit, and rings for 250.
        yield return HangingBell(new Vector2(2.9f, 8.95f), 0.55f);

        // The multiplier's inserts (lit up to its count) and the shoot-again light (lit while the ball save is on).
        yield return Inserts();

        var plunger = new Entity("Plunger");
        plunger.Transform.Position = new Vector3((Divider + 0.15f + Right) / 2, -8, 0);
        MakePlunger(plunger);
        yield return plunger;
        yield return Ball(new Vector2((Divider + 0.15f + Right) / 2, -8 + BallRadius + 0.01f));

        // The drain under the flippers and the outlanes, three balls and a ball save; a floor under it.
        var drain = new Entity("Drain");
        drain.Transform.Position = new Vector3(Middle, -9.7f, 0);
        var mouth = drain.AddComponent<BoxCollider>();
        (mouth.Size, mouth.IsTrigger) = (new Vector2(Divider - Left, 1), true);
        var draining = drain.AddComponent<Drain>();
        (draining.Balls, draining.BallSave, draining.Within) = (Balls, BallSave, "Table");
        (draining.Sound, draining.SaveSound) = (PinballArt.DrainSound, PinballArt.SaveSound);
        (draining.DrainMessage, draining.SaveMessage, draining.GameOverMessage) = (BallLost, BallSaved, GameOver);
        var extra = drain.AddComponent<Trigger>();
        (extra.Message, extra.Do) = (ExtraBall, "ExtraBall");
        var ding = drain.AddComponent<SoundPlayer>();
        (ding.Message, ding.Sound) = (ExtraBall, ExtraBallSound);
        yield return drain;
        yield return Wall("Floor", new Vector2(Left - 0.4f, -10.6f), new Vector2(Right + 0.4f, -10.6f), 0.4f);
    }

    // Each side's outlane post (the left one's x; the right one's mirrored), the left kicker's middle, and the skyway's wall.
    private const float OutlanePost = -4.2f;
    private const float KickerX = -2.9f;
    private const float SkywayWall = 2.7f;

    private static Entity Post(string name, float x, float bottom, float top, float thick) =>
        PinballParts.Post(name, x, bottom, top, thick, MidwayArt.Insert);

    private static readonly Color Chrome = Color.FromBytes(0xA8, 0xB4, 0xC8);

    // Mirrored across the playfield's middle, left of the shooter lane.
    private static float Mirror(float x) => 2 * Middle - x;

    // A lane on its own in a channel: lit for a moment as a ball goes through, scoring `points`.
    private static Entity ChannelLane(string name, Vector2 at, int points)
    {
        var lane = new Entity(name);
        lane.Transform.Position = new Vector3(at, 0);
        var sprite = lane.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Tint) = (PinballArt.Light, new Vector2(0.5f), Pink);
        var mouth = lane.AddComponent<BoxCollider>();
        (mouth.Size, mouth.IsTrigger) = (new Vector2(0.75f, 1), true);
        var rollover = lane.AddComponent<Lane>();
        (rollover.Points, rollover.Sound, rollover.FlipperChanges) = (points, PinballArt.LaneSound, false);
        (rollover.CompleteMessage, rollover.CompletePoints, rollover.ResetAfter) = (null, 0, 0.6f);
        return lane;
    }

    // A brass bell on a rope from `top` (in the world), `drop` below it: a target that swings.
    private static Entity HangingBell(Vector2 top, float drop)
    {
        var bell = new Entity("Bell");
        bell.Transform.Position = new Vector3(top - new Vector2(0, drop + 0.3f), 0);
        var sprite = bell.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (MidwayArt.Bell, new Vector2(0.7f), 2);
        // Its outline is the bell's, not round: only balls are round (a drain counts every round body on the table as a ball).
        bell.AddComponent<PolygonCollider>().Points = "-0.12 0.3; 0.12 0.3; 0.22 0.1; 0.25 -0.15; 0.32 -0.25; -0.32 -0.25; -0.25 -0.15; -0.22 0.1";
        var body = bell.AddComponent<Body>();
        (body.Rotates, body.Mass, body.Bounciness) = (true, 0.6f, 0.2f);
        var rope = bell.AddComponent<Rope>();
        (rope.Anchor, rope.OtherAnchor, rope.Climbable) = (new Vector2(0, 0.3f), top, false);
        var target = bell.AddComponent<Target>();
        (target.Points, target.Sound, target.CompleteMessage, target.CompletePoints, target.ResetAfter) = (250, BellSound, null, 0, 0.4f);
        return bell;
    }

    // The multiplier's five inserts in a row (1x to 5x, lit up to its count) and the shoot-again light under them.
    private static Entity Inserts()
    {
        var inserts = new Entity("Inserts");
        inserts.Transform.Position = new Vector3(InsertsAt, 0);
        var row = new Entity("Multiplier Lights");
        var stars = row.AddComponent<Stars>();
        (stars.Track, stars.Output, stars.Count, stars.Image, stars.Size, stars.Spacing) = ("Multiplier", "Count", MaxMultiplier, MidwayArt.Insert, 0.5f, 0.3f);
        (stars.Color, stars.EmptyColor, stars.Rim, stars.Order, stars.Delay) = (Gold, new Color(0.35f, 0.3f, 0.5f, 0.7f), Color.Transparent, -10, 0.08f);
        Put(inserts, row);
        for (var i = 0; i < MaxMultiplier; i++)
        {
            var label = Label($"{i + 1}x", new Vector2((i - 2) * 0.8f, -0.5f), $"{i + 1}x", 0.26f, Dim);
            label.GetComponent<Text>()!.Order = -9;
            Put(inserts, label);
        }

        var shootAgain = new Entity("Shoot Again");
        shootAgain.Transform.Position = new Vector3(0, -1.3f, 0);
        var lamp = shootAgain.AddComponent<Stars>();
        (lamp.Track, lamp.Output, lamp.Count, lamp.Image, lamp.Size) = ("Drain", "Saving", 1, MidwayArt.Insert, 0.45f);
        (lamp.Color, lamp.EmptyColor, lamp.Rim, lamp.Order) = (Color.FromBytes(0x7C, 0xF0, 0x9A), new Color(0.3f, 0.35f, 0.4f, 0.6f), Color.Transparent, -10);
        Put(inserts, shootAgain);
        var words = Label("Shoot Again Label", new Vector2(0, -1.75f), "SHOOT AGAIN", 0.22f, Dim);
        words.GetComponent<Text>()!.Order = -9;
        Put(inserts, words);
        return inserts;
    }

    // The score (multiplied by the multiplier, the best kept), the multiplier (the top lanes raise it to 5x, a lost ball puts
    // it back to 1x), the drop target banks (the second down gives an extra ball, once a game) and the callouts.
    private static void AddScoring(Scene scene)
    {
        var score = Corner(Label("Score", Vector2.Zero, "0", 1.2f, Light), Anchor.TopLeft, new Vector2(0.7f, -2.1f));
        var counter = score.AddComponent<Counter>();
        (counter.Message, counter.Format, counter.MultiplyBy, counter.SaveBestAs, counter.NewBestMessage) = ("Score", "{0:N0}", "Multiplier", BestKey, NewBest);
        foreach (var message in new[] { LanesLit, TargetsDown })
        {
            var bonus = score.AddComponent<Trigger>();
            (bonus.Message, bonus.Do, bonus.UseMessageNumber) = (message, "Add", true);
        }

        var sting = score.AddComponent<SoundPlayer>();
        (sting.Message, sting.Sound) = (NewBest, NewBestSound);
        scene.Add(score);

        var multiplier = Corner(Label("Multiplier", Vector2.Zero, "1x", 0.55f, Gold), Anchor.TopLeft, new Vector2(0.7f, -4.35f));
        var times = multiplier.AddComponent<Counter>();
        (times.Message, times.OnePerMessage, times.Value, times.Max, times.Format) = (LanesLit, true, 1, MaxMultiplier, "Multiplier {0}x");
        var back = multiplier.AddComponent<Trigger>();
        (back.Message, back.Do, back.With) = (BallLost, "Set", "1");
        var up = multiplier.AddComponent<SoundPlayer>();
        (up.Message, up.Sound) = (LanesLit, MultiplierSound);
        scene.Add(multiplier);

        var banks = new Entity("Target Banks");
        var down = banks.AddComponent<Counter>();
        (down.Message, down.OnePerMessage, down.Format, down.Reaches, down.Reach, down.ReachMessage) = (TargetsDown, true, null, CounterReach.AtLeast, 2, ExtraBall);
        scene.Add(banks);

        // Callouts over the table: a ball saved, an extra ball, a new best.
        var callouts = new Entity("Callouts");
        callouts.Transform.Position = new Vector3(Middle, -1.4f, 0);
        foreach (var (message, prefab) in new[] { (BallSaved, "Ball Saved"), (ExtraBall, "Extra Ball"), (NewBest, "New Best") })
        {
            var spawner = new Entity($"{prefab} Callout");
            var spawns = spawner.AddComponent<Spawner>();
            (spawns.Prefab, spawns.Running, spawns.MaxAlive) = (prefab, false, 1);
            var on = spawner.AddComponent<Trigger>();
            (on.Message, on.Do) = (message, "Spawn");
            Put(callouts, spawner);
        }

        scene.Add(callouts);
    }

    // A word that pops up over the table and fades.
    private static Entity Callout(string name, string words, Color color)
    {
        var callout = Label(name, Vector2.Zero, words, 0.9f, color);
        callout.GetComponent<Text>()!.Order = 900;
        var life = callout.AddComponent<Lifetime>();
        (life.Seconds, life.FadeOut) = (1.8f, 0.6f);
        return callout;
    }

    // Down the left: the table's name, the score, the best, which ball it is and the multiplier; the controls on the right.
    private static void AddHud(Scene scene)
    {
        scene.Add(Corner(Label("Logo", Vector2.Zero, "MIDNIGHT MIDWAY", 0.6f, Pink), Anchor.TopLeft, new Vector2(0.7f, -0.8f)));

        var best = Corner(Label("Best", Vector2.Zero, "Best {0:N0}", 0.45f, Dim), Anchor.TopLeft, new Vector2(0.7f, -2.95f));
        (best.GetComponent<Text>()!.Track, best.GetComponent<Text>()!.Output) = ("Score", "Best");
        scene.Add(best);

        var ball = Corner(Label("Balls", Vector2.Zero, "Ball {0} of {1}", 0.55f, Light), Anchor.TopLeft, new Vector2(0.7f, -3.65f));
        (ball.GetComponent<Text>()!.Track, ball.GetComponent<Text>()!.Output) = ("Drain", "Ball");
        scene.Add(ball);

        var help = Corner(Label("Controls", Vector2.Zero,
            "Hold {Plunger} and let go to shoot\nLeft and Right Shift (or Z and /) to flip\nClick or tap either side to flip\n{Nudge} to nudge, but not too often\n\nLight M, I and D to raise the multiplier\nKnock the targets down twice for an extra ball\n\n{Pause} to pause",
            0.38f, Dim), Anchor.TopRight, new Vector2(-0.7f, -3.9f));
        help.GetComponent<Text>()!.Align = TextAlign.Right;
        scene.Add(help);
    }

    private static Entity Corner(Entity label, Anchor anchor, Vector2 offset)
    {
        var text = label.GetComponent<Text>()!;
        (text.Align, text.Order) = (anchor is Anchor.TopRight ? TextAlign.Right : TextAlign.Left, 150);
        var corner = label.AddComponent<ScreenAnchor>();
        (corner.Anchor, corner.Offset) = (anchor, offset);
        return label;
    }

    // Opens as the last ball drains, with its jingle: the score, the best, "New best!" if it is one, and Play Again or Title.
    private static Entity GameOverPanel()
    {
        var panel = new Entity("Game Over");
        panel.Transform.Position = new Vector3(0, 0, 2);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var shown = panel.AddComponent<Scenes.Panel>();
        (shown.OpenMessage, shown.CloseOnBack, shown.PauseGame, shown.Sound) = (GameOver, false, false, GameOverSound);
        var backdrop = panel.AddComponent<Sprite>();
        (backdrop.Size, backdrop.Tint, backdrop.Order) = (new Vector2(9, 6.6f), Panel, 400);

        WithChild(panel, PanelLabel("Heading", new Vector2(0, 2.3f), "GAME OVER", 1.1f, Pink));
        var final = PanelLabel("Final Score", new Vector2(0, 1.0f), "{0:N0}", 0.9f, Light);
        (final.GetComponent<Text>()!.Track, final.GetComponent<Text>()!.Output) = ("Score", "Count");
        WithChild(panel, final);
        var best = PanelLabel("Best Score", new Vector2(0, 0.1f), "Best {0:N0}", 0.45f, Dim);
        (best.GetComponent<Text>()!.Track, best.GetComponent<Text>()!.Output) = ("Score", "Best");
        WithChild(panel, best);
        var newBest = PanelLabel("New Best", new Vector2(0, -0.6f), "{0:'New best!';;''}", 0.55f, Gold);
        (newBest.GetComponent<Text>()!.Track, newBest.GetComponent<Text>()!.Output) = ("Score", "New Best");
        WithChild(panel, newBest);

        WithChild(panel, Fit(MenuButton("Play Again", new Vector2(-1.9f, -2.0f), ButtonAction.RestartScene, null, Key.Enter, Violet, order: 410)));
        WithChild(panel, Fit(MenuButton("Title", new Vector2(1.9f, -2.0f), ButtonAction.LoadScene, "Main", Key.Escape, Gray, order: 410)));
        return panel;
    }

    private static Entity PanelLabel(string name, Vector2 at, string value, float size, Color color)
    {
        var label = Label(name, at, value, size, color);
        label.GetComponent<Text>()!.Order = 401;
        return label;
    }

    private static Entity Fit(Entity button)
    {
        var size = new Vector2(3.4f, 1);
        button.GetComponent<Sprite>()!.Size = size;
        button.GetComponent<Button>()!.Size = size;
        button.GetComponent<Text>()!.Size = 0.5f;
        return button;
    }

    // ----- The title screen: the midway at night, the best score, Play and Quit, and the controls

    private static Scene TitleScreen()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        (view.Size, view.Background, view.Transition) = (8, Night, SceneTransition.Fade);
        scene.Add(camera);

        var backdrop = new Entity("Midway");
        backdrop.Transform.Position = new Vector3(0, 0, -5);
        var picture = backdrop.AddComponent<Sprite>();
        (picture.Image, picture.Size, picture.Order) = (MidwayArt.Backdrop, new Vector2(32, 17.6f), -40);
        scene.Add(backdrop);

        // A little table leaning in the corner, with its ball.
        var field = new Entity("Little Table");
        field.Transform.Position = new Vector3(-9.5f, -1.2f, 0);
        field.Transform.Rotation = new Vector3(0, 0, 8);
        var fieldPicture = field.AddComponent<Sprite>();
        (fieldPicture.Image, fieldPicture.Size, fieldPicture.Order) = (MidwayArt.Playfield, new Vector2(5, 9), -10);
        scene.Add(field);
        var bumper = new Entity("Bumper");
        bumper.Transform.Position = new Vector3(9.5f, 0.6f, 0);
        var bumperPicture = bumper.AddComponent<Sprite>();
        (bumperPicture.Image, bumperPicture.Size, bumperPicture.Order) = (PinballArt.Bumper, new Vector2(2.4f), -9);
        scene.Add(bumper);
        var ball = new Entity("Ball");
        ball.Transform.Position = new Vector3(8.4f, 2.1f, 0);
        var ballPicture = ball.AddComponent<Sprite>();
        (ballPicture.Image, ballPicture.Size, ballPicture.Order) = (PinballArt.Ball, new Vector2(1), -8);
        scene.Add(ball);

        scene.Add(Label("Title", new Vector2(0, 4.6f), "MIDNIGHT MIDWAY", 1.6f, Pink));
        scene.Add(Label("Subtitle", new Vector2(0, 3.4f), "a pinball table at the carnival", 0.5f, Dim));

        var best = Label("Best", new Vector2(0, 2.3f), "", 0.6f, Gold);
        var counter = best.AddComponent<Counter>();
        (counter.Message, counter.SaveBestAs, counter.Format, counter.Pop) = (null, BestKey, "{1:'Best: '#,##0;;'No best yet'}", 1);
        scene.Add(best);

        scene.Add(MenuButton("Play", new Vector2(0, 0.7f), ButtonAction.LoadScene, TableScene, Key.Enter, Violet));
        scene.Add(MenuButton("Quit", new Vector2(0, -0.7f), ButtonAction.Quit, null, Key.Escape, Gray));

        var controls = Label("Controls", new Vector2(0, -3.6f),
            "Shoot: hold Space or the down arrow and let go  ·  Flip: Left and Right Shift, or Z and /\nNudge: N or the up arrow  ·  Mouse or a finger: click or tap either side to flip, hold the plunger to pull it\nController: the shoulders or triggers flip, hold A or pull the right stick down to shoot, B nudges",
            0.34f, Dim);
        scene.Add(controls);

        var hint = Label("Hint", Vector2.Zero, "Arrows to choose  ·  Enter to play", 0.4f, Dim);
        var anchor = hint.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.Bottom, new Vector2(0, 0.5f));
        scene.Add(hint);
        return scene;
    }
}
