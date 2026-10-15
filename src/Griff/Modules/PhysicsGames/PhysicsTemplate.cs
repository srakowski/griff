using System.Globalization;
using System.Numerics;
using Griff.Audio;
using Griff.Data;
using Griff.Input;
using Griff.Projects;
using Griff.Scenes;
using static Griff.Projects.ProjectTemplates;

namespace Griff.Modules.PhysicsGames;

// The Physics template, in the style of a slingshot game: a menu of levels (each opening once the one before is won, with
// the best stars and score each has had), and levels where a slingshot launches birds at towers of wood, glass and stone
// to knock out the green grubs inside. Each block's strength, points, look and sounds come from the Materials table
// through Table Values; a Level Goal says when it's won (counting a bonus for each bird left and giving one to three stars)
// or lost, and keeps the best in the save data. All of it no-code components.
internal sealed class PhysicsTemplate : ProjectTemplate
{
    public override string Title => "Physics";

    public override string Description => "Pull the slingshot back and let go to knock down towers of wood, glass and stone and the grubs inside, in three levels, each opening once the one before is won. A bonus for every bird left, one to three stars, and the best kept. Drag the bird back with the mouse or a finger, or pull with the arrows (or a stick) and let go with Space (or A); R to try again.";

    public override int Order => 85;

    protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Bright, seed: 27) with { Volume = 0.5f };

    protected override Scene BuildStartScene() => Menu();

    protected override Dictionary<string, Scene> BuildMoreScenes() =>
        Enumerable.Range(0, Levels.Count).ToDictionary(i => $"Scenes/{LevelScene(i)}.scene", Level);

    protected override Dictionary<string, Entity> BuildPrefabs() => new()
    {
        ["Prefabs/Bird.prefab"] = Bird(Vector2.Zero),
        ["Prefabs/Boulder.prefab"] = Boulder(Vector2.Zero),
    };

    public override IReadOnlyDictionary<string, byte[]> Images() => PhysicsArt.Files();

    public override IReadOnlyDictionary<string, Table> Tables() => new Dictionary<string, Table>
    {
        [$"Tables/{MaterialsTable}.table"] = TableFile.Read(MaterialsJson, MaterialsTable),
    };

    // Its own sounds: the slingshot stretching and twanging, wood, glass and stone knocked and broken, a grub popping, and
    // a star.
    protected override IReadOnlyDictionary<string, SoundRecipe> MoreSounds => new Dictionary<string, SoundRecipe>
    {
        [StretchSound] = new() { Wave = Waveform.Sawtooth, Frequency = 0.18f, Slide = 0.12f, Sustain = 0.12f, Decay = 0.12f, LowPassCutoff = 0.45f, Volume = 0.3f },
        [TwangSound] = new() { Wave = Waveform.Triangle, Frequency = 0.34f, Slide = -0.22f, VibratoDepth = 0.45f, VibratoSpeed = 0.6f, Sustain = 0.08f, Decay = 0.3f, Punch = 0.3f, Volume = 0.5f },
        [KnockSound] = new() { Wave = Waveform.Noise, Frequency = 0.22f, Slide = -0.1f, Sustain = 0.02f, Decay = 0.12f, Punch = 0.4f, LowPassCutoff = 0.55f, Volume = 0.45f },
        [CrackSound] = new() { Wave = Waveform.Noise, Frequency = 0.36f, Slide = -0.25f, Sustain = 0.06f, Decay = 0.26f, Punch = 0.55f, LowPassCutoff = 0.8f, Volume = 0.55f },
        [ClinkSound] = new() { Wave = Waveform.Square, SquareDuty = 0.3f, Frequency = 0.78f, Sustain = 0.02f, Decay = 0.14f, Punch = 0.2f, HighPassCutoff = 0.25f, Volume = 0.3f },
        [ShatterSound] = new() { Wave = Waveform.Noise, Frequency = 0.82f, Slide = -0.08f, Sustain = 0.05f, Decay = 0.4f, Punch = 0.45f, RepeatSpeed = 0.55f, HighPassCutoff = 0.3f, Volume = 0.5f },
        [ThudSound] = new() { Wave = Waveform.Noise, Frequency = 0.09f, Slide = -0.05f, Sustain = 0.03f, Decay = 0.16f, Punch = 0.6f, LowPassCutoff = 0.4f, Volume = 0.6f },
        [CrumbleSound] = new() { Wave = Waveform.Noise, Frequency = 0.13f, Slide = -0.06f, Sustain = 0.16f, Decay = 0.45f, Punch = 0.6f, RepeatSpeed = 0.62f, LowPassCutoff = 0.5f, Volume = 0.65f },
        [PopSound] = new() { Wave = Waveform.Square, SquareDuty = 0.4f, Frequency = 0.36f, Slide = 0.32f, Sustain = 0.05f, Decay = 0.2f, Punch = 0.45f, Volume = 0.45f },
        [StarSound] = new() { Wave = Waveform.Square, SquareDuty = 0.2f, Frequency = 0.58f, ChangeAmount = 0.3f, ChangeSpeed = 0.55f, Sustain = 0.08f, Decay = 0.32f, Punch = 0.4f, Volume = 0.45f },
    };

    public const string StretchSound = "Sounds/stretch.wav";
    public const string TwangSound = "Sounds/twang.wav";
    public const string KnockSound = "Sounds/knock.wav";
    public const string CrackSound = "Sounds/crack.wav";
    public const string ClinkSound = "Sounds/clink.wav";
    public const string ShatterSound = "Sounds/shatter.wav";
    public const string ThudSound = "Sounds/thud.wav";
    public const string CrumbleSound = "Sounds/crumble.wav";
    public const string PopSound = "Sounds/pop.wav";
    public const string StarSound = "Sounds/star.wav";

    // The slingshot twangs and stretches; the level counts its bonus, and is won or lost, out loud; stars ping in.
    protected internal override void WireSounds(Entity entity)
    {
        foreach (var component in entity.Components)
        {
            switch (component)
            {
                case Slingshot slingshot:
                    TemplateSounds.Set(slingshot.LaunchSound, s => slingshot.LaunchSound = s, TwangSound);
                    TemplateSounds.Set(slingshot.StretchSound, s => slingshot.StretchSound = s, StretchSound);
                    break;
                case LevelGoal goal:
                    TemplateSounds.Set(goal.BonusSound, s => goal.BonusSound = s, TemplateSounds.Coin);
                    TemplateSounds.Set(goal.WinSound, s => goal.WinSound = s, TemplateSounds.PowerUp);
                    TemplateSounds.Set(goal.LoseSound, s => goal.LoseSound = s, TemplateSounds.Hurt);
                    break;
                case Stars stars when stars.Delay > 0:
                    TemplateSounds.Set(stars.Sound, s => stars.Sound = s, StarSound);
                    break;
            }
        }

        base.WireSounds(entity);
    }

    // ----- The levels

    /// <summary>The table every block's material comes from.</summary>
    public const string MaterialsTable = "Materials";

    /// <summary>What the levels' targets are called: a Level Goal counts them.</summary>
    public const string TargetName = "Grub";

    /// <summary>A block's material: a row of the Materials table.</summary>
    public enum Material
    {
        Wood,
        Glass,
        Stone,
    }

    /// <summary>A block's shape: a plank (stood on end for a post), a square block, or a triangle with its point up.</summary>
    public enum Shape
    {
        Plank,
        Block,
        Wedge,
    }

    /// <summary>
    /// A block of a level: its shape and material, its middle, its size (a plank's length then its thickness) and how far
    /// it's turned (90 stands a plank on end as a post).
    /// </summary>
    public sealed record Piece(Shape Shape, Material Material, Vector2 At, Vector2 Size, float Turn = 0);

    /// <summary>
    /// A level: its name, its blocks, where its grubs sit, its rock (solid ground that doesn't move: its middle and size),
    /// a stone weight hanging from a beam on a rope (its middle, and the beam's name), the shots its
    /// slingshot has (birds, then boulders), and the scores that earn two and three stars.
    /// </summary>
    public sealed record LevelData(
        string Name,
        Piece[] Pieces,
        Vector2[] Grubs,
        (Vector2 At, Vector2 Size)[] Rocks,
        int Birds,
        int Boulders,
        int TwoStars,
        int ThreeStars,
        (Vector2 At, string Beam)? Weight = null);

    private const float Thick = 0.4f;

    private static Piece Post(Material material, float x, float bottom, float height) => new(Shape.Plank, material, new Vector2(x, bottom + height / 2), new Vector2(height, Thick), 90);

    private static Piece Plank(Material material, float x, float bottom, float length, float thick = Thick) => new(Shape.Plank, material, new Vector2(x, bottom + thick / 2), new Vector2(length, thick));

    private static Piece Block(Material material, float x, float bottom, float size) => new(Shape.Block, material, new Vector2(x, bottom + size / 2), new Vector2(size));

    private static Piece Wedge(Material material, float x, float bottom, float width, float height) => new(Shape.Wedge, material, new Vector2(x, bottom + height / 2), new Vector2(width, height));

    /// <summary>The levels, in order.</summary>
    public static IReadOnlyList<LevelData> Levels { get; } =
    [
        // Two storeys of wood with a grub on each floor and a roof on top.
        new("Timber",
        [
            Post(Material.Wood, 6.8f, 0, 2.4f),
            Post(Material.Wood, 9.2f, 0, 2.4f),
            Plank(Material.Wood, 8, 2.4f, 3.2f),
            Post(Material.Wood, 7.2f, 2.8f, 2),
            Post(Material.Wood, 8.8f, 2.8f, 2),
            Plank(Material.Wood, 8, 4.8f, 2.4f),
            Wedge(Material.Wood, 8, 5.2f, 2, 1),
        ],
        [new(8, 0), new(8, 2.8f)],
        [],
        Birds: 3, Boulders: 0, TwoStars: 22000, ThreeStars: 32000),

        // A glasshouse of two floors with a grub on each, and one more on a rock beside it behind panes of glass.
        new("Glasshouse",
        [
            Post(Material.Glass, 5.6f, 0, 1.6f),
            Post(Material.Glass, 7.4f, 0, 1.6f),
            Plank(Material.Glass, 6.5f, 1.6f, 2.4f, 0.3f),
            Post(Material.Wood, 4.8f, 0, 2.6f),
            Post(Material.Wood, 8.2f, 0, 2.6f),
            Plank(Material.Wood, 6.5f, 2.6f, 4),
            Post(Material.Wood, 5.4f, 3, 1.8f),
            Post(Material.Wood, 7.6f, 3, 1.8f),
            Plank(Material.Glass, 6.5f, 4.8f, 2.8f, 0.3f),
            Wedge(Material.Wood, 6.5f, 5.1f, 2.4f, 1),
            Block(Material.Glass, 11.9f, 3, 0.8f),
            Block(Material.Glass, 13.1f, 3, 0.8f),
            Plank(Material.Glass, 12.5f, 3.8f, 2, 0.3f),
        ],
        [new(6.5f, 0), new(6.5f, 3), new(12.5f, 3)],
        [(new Vector2(12.5f, 1.5f), new Vector2(2.2f, 3))],
        Birds: 3, Boulders: 0, TwoStars: 18000, ThreeStars: 29000),

        // A stone keep with a grub under its floor and one on it, and a stone weight hanging over a third from a beam laid
        // across two rocks: break the beam and down it comes.
        new("Stone Keep",
        [
            Block(Material.Stone, 5.4f, 0, 1),
            Block(Material.Stone, 8.6f, 0, 1),
            Plank(Material.Stone, 7, 1, 4.2f),
            Post(Material.Wood, 5.8f, 1.4f, 2),
            Post(Material.Wood, 8.2f, 1.4f, 2),
            Plank(Material.Wood, 7, 3.4f, 3.2f),
            Wedge(Material.Stone, 7, 3.8f, 2.4f, 1),
            Plank(Material.Wood, 12.1f, 4, 3.8f),
        ],
        [new(7, 0), new(7, 1.4f), new(12.1f, 0)],
        [(new Vector2(10.6f, 2), new Vector2(0.8f, 4)), (new Vector2(13.6f, 2), new Vector2(0.8f, 4))],
        Birds: 3, Boulders: 1, TwoStars: 27000, ThreeStars: 38000,
        Weight: (new Vector2(12.1f, 2.35f), "Beam")),
    ];

    /// <summary>The scene each level is saved as, e.g. "Level1".</summary>
    public static string LevelScene(int index) => $"Level{index + 1}";

    /// <summary>What a level's best is kept under in the save data: "Level1.Stars" and "Level1.Best".</summary>
    public static string SaveKey(int index) => LevelScene(index);

    private static readonly Color Sky = Color.FromBytes(0x7E, 0xC4, 0xF0);
    private static readonly Color Gold = Color.FromBytes(0xF8, 0xCC, 0x3A);
    private static readonly Color Light = Color.FromBytes(0xFA, 0xF6, 0xEC);
    private static readonly Color Ink = Color.FromBytes(0x2A, 0x2A, 0x3A);
    private static readonly Color Panel = new(0.1f, 0.12f, 0.2f, 0.92f);
    private static readonly Color Red = Color.FromBytes(0xD8, 0x44, 0x3C);
    private static readonly Color Green = Color.FromBytes(0x4C, 0xA0, 0x44);
    private static readonly Color Blue = Color.FromBytes(0x3C, 0x7C, 0xD8);
    private static readonly Color Gray = Color.FromBytes(0x4A, 0x50, 0x60);

    // Each level's slingshot stands here, on a mound of rock (so it can be pulled well down to launch high), its shots in a
    // row behind it.
    private const float SlingshotX = -11;
    private const float MoundHeight = 1.2f;

    // The level's edges, which the camera stays inside: from behind the slingshot to past the towers, and from just under
    // the ground to high in the sky.
    private static readonly Vector2 WorldMin = new(-18, -3);
    private static readonly Vector2 WorldMax = new(20, 16);

    private static Scene Level(int index)
    {
        var data = Levels[index];
        var scene = new Scene();
        AddCamera(scene, follow: true);
        AddScenery(scene);

        scene.Add(Rock(new Vector2(SlingshotX - 2.2f, MoundHeight / 2), new Vector2(7, MoundHeight)));
        foreach (var (at, size) in data.Rocks)
            scene.Add(Rock(at, size));

        // The blocks, each a Breakable whose material comes from the table; a beam the weight hangs from is called so.
        var beamAt = data.Weight?.At;
        foreach (var piece in data.Pieces)
        {
            var block = Block(piece);
            if (beamAt is { } weightAt && piece.Shape == Shape.Plank && piece.Turn == 0 && MathF.Abs(piece.At.X - weightAt.X) < 0.01f &&
                piece.At.Y > weightAt.Y)
                block.Name = data.Weight!.Value.Beam;
            scene.Add(block);
        }

        foreach (var at in data.Grubs)
            scene.Add(Grub(at));
        if (data.Weight is var (weight, beam))
            scene.Add(Weight(weight, beam));

        // The slingshot with its shots waiting behind it: birds, then boulders.
        var slingshot = new Entity("Slingshot");
        slingshot.Transform.Position = new Vector3(SlingshotX, MoundHeight, 0);
        var sling = slingshot.AddComponent<Slingshot>();
        (sling.Power, sling.MaxPull, sling.GrabRadius, sling.LoadDelay) = (28, 2.2f, 1.4f, 1);
        (sling.LaunchMessage, sling.OutOfShotsMessage) = (null, null);
        scene.Add(slingshot);
        for (var i = 0; i < data.Birds + data.Boulders; i++)
            scene.Add(i < data.Birds ? Bird(new Vector2(-1 - i, 0.4f)) : Boulder(new Vector2(-1 - i, 0.5f)), slingshot);

        // What it's for: knock out every grub, the score keeping track.
        var level = new Entity("Level");
        var goal = level.AddComponent<LevelGoal>();
        (goal.Targets, goal.Slingshot, goal.Score, goal.SaveAs) = (TargetName, "Slingshot", "Score", SaveKey(index));
        (goal.TwoStars, goal.ThreeStars) = (data.TwoStars, data.ThreeStars);
        scene.Add(level);

        AddHud(scene, index, data);
        scene.Add(CompletePanel(index));
        scene.Add(FailedPanel());
        AddPauseMenu(scene, menuScene: "Main");
        return scene;
    }

    // The camera, sky blue: in a level, following the slingshot (and each shot as it flies), never past the level's edges.
    private static void AddCamera(Scene scene, bool follow)
    {
        var camera = new Entity("Main Camera");
        camera.Transform.Position = new Vector3(-3, 5, 0);
        var view = camera.AddComponent<Camera>();
        (view.Size, view.Background, view.Transition) = (8, Sky, SceneTransition.Fade);
        if (follow)
        {
            var track = camera.AddComponent<Follow>();
            (track.Target, track.Smoothing, track.Offset, track.StayInside) = ("Slingshot", 3, new Vector2(-SlingshotX, 4.8f), "Bounds");
        }

        scene.Add(camera);
    }

    // Clouds and hills far behind, moving slower than the level, the ground underfoot, and the level's bounds.
    private static void AddScenery(Scene scene)
    {
        var bounds = new Entity("Bounds");
        bounds.Transform.Position = new Vector3((WorldMin + WorldMax) / 2, -5);
        var area = bounds.AddComponent<BoxCollider>();
        (area.Size, area.IsTrigger) = (WorldMax - WorldMin, true);
        scene.Add(bounds);

        scene.Add(Layer("Clouds", PhysicsArt.Clouds, new Vector2(0, 11.5f), new Vector2(36, 9), new Vector2(0.15f, 0.8f), -30));
        scene.Add(Layer("Hills", PhysicsArt.Hills, new Vector2(0, 4.5f), new Vector2(40, 10), new Vector2(0.45f, 1), -20));

        var ground = new Entity("Ground");
        ground.Transform.Position = new Vector3(1, -2, 0);
        var sprite = ground.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (PhysicsArt.Ground, new Vector2(44, 4), -5);
        ground.AddComponent<BoxCollider>().Size = new Vector2(44, 4);
        scene.Add(ground);
    }

    private static Entity Layer(string name, string image, Vector2 at, Vector2 size, Vector2 factor, int order)
    {
        var layer = new Entity(name);
        layer.Transform.Position = new Vector3(at, -10);
        var sprite = layer.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (image, size, order);
        var parallax = layer.AddComponent<Parallax>();
        (parallax.Factor, parallax.RepeatX) = (factor, true);
        return layer;
    }

    // Solid rock that doesn't move, to stand things on.
    private static Entity Rock(Vector2 at, Vector2 size)
    {
        var rock = new Entity("Rock");
        rock.Transform.Position = new Vector3(at, 0);
        var sprite = rock.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (PhysicsArt.Rock, size, -2);
        rock.AddComponent<BoxCollider>().Size = size;
        return rock;
    }

    // How heavy each material is for its size, and how much more of the table's Points a piece is worth for its size.
    private static float Density(Material material) => material switch
    {
        Material.Glass => 0.7f,
        Material.Stone => 2.5f,
        _ => 1,
    };

    // A block: its picture, a collider its shape, a body that rotates (asleep until something knocks it), and a Breakable
    // whose strength, points, sounds and picture are its material's row of the Materials table.
    private static Entity Block(Piece piece)
    {
        var (shape, material, at, size, turn) = piece;
        var entity = new Entity($"{material} {shape}");
        entity.Transform.Position = new Vector3(at, 0);
        entity.Transform.Rotation = new Vector3(0, 0, turn);
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Size, sprite.Order) = (size, material == Material.Glass ? 2 : 1);
        if (shape == Shape.Wedge)
        {
            var outline = entity.AddComponent<PolygonCollider>();
            outline.Points = string.Create(CultureInfo.InvariantCulture, $"{-size.X / 2} {-size.Y / 2}; {size.X / 2} {-size.Y / 2}; 0 {size.Y / 2}");
        }
        else
        {
            entity.AddComponent<BoxCollider>().Size = size;
        }

        var body = entity.AddComponent<Body>();
        var area = shape == Shape.Wedge ? size.X * size.Y / 2 : size.X * size.Y;
        (body.Rotates, body.Mass, body.Asleep, body.Friction) = (true, MathF.Round(Density(material) * area, 2), true, 0.6f);

        var breakable = entity.AddComponent<Breakable>();
        breakable.BreakMessage = "Score";
        breakable.ShowPoints = true;
        breakable.Split = shape == Shape.Block ? 2 : shape == Shape.Wedge ? 2 : 3;
        var row = Row(material);
        (sprite.Image, breakable.Strength, breakable.MinHit, breakable.Points, breakable.Sound, breakable.HitSound) =
            (shape switch { Shape.Block => row.Block, Shape.Wedge => row.Wedge, _ => row.Plank }, row.Strength, row.MinHit, row.Points, row.Sound, row.HitSound);
        var values = entity.AddComponent<TableValues>();
        (values.Table, values.Row) = (MaterialsTable, material.ToString());
        values.Sets =
        [
            $"Sprite.Image = {shape} Picture",
            "Breakable.Strength = Strength",
            "Breakable.MinHit = Min Hit",
            "Breakable.Points = Points",
            "Breakable.Sound = Sound",
            "Breakable.HitSound = Hit Sound",
        ];
        return entity;
    }

    // A material's row of the Materials table, as the scenes are written with it (the game reads the table).
    private static (string Plank, string Block, string Wedge, float Strength, float MinHit, int Points, string Sound, string HitSound) Row(Material material) => material switch
    {
        Material.Glass => (PhysicsArt.GlassPlank, PhysicsArt.GlassBlock, PhysicsArt.GlassBlock, 4, 1.5f, 300, ShatterSound, ClinkSound),
        Material.Stone => (PhysicsArt.StonePlank, PhysicsArt.StoneBlock, PhysicsArt.StoneWedge, 40, 6, 1000, CrumbleSound, ThudSound),
        _ => (PhysicsArt.WoodPlank, PhysicsArt.WoodBlock, PhysicsArt.WoodWedge, 12, 3, 500, CrackSound, KnockSound),
    };

    private static readonly string MaterialsJson = $$"""
        {
          "columns": [
            { "name": "Strength", "type": "number" },
            { "name": "Min Hit", "type": "number" },
            { "name": "Points", "type": "whole number" },
            { "name": "Plank Picture", "type": "sprite" },
            { "name": "Block Picture", "type": "sprite" },
            { "name": "Wedge Picture", "type": "sprite" },
            { "name": "Sound", "type": "sound" },
            { "name": "Hit Sound", "type": "sound" }
          ],
          "rows": {
            "Wood": { "Strength": 12, "Min Hit": 3, "Points": 500, "Plank Picture": "{{PhysicsArt.WoodPlank}}", "Block Picture": "{{PhysicsArt.WoodBlock}}", "Wedge Picture": "{{PhysicsArt.WoodWedge}}", "Sound": "{{CrackSound}}", "Hit Sound": "{{KnockSound}}" },
            "Glass": { "Strength": 4, "Min Hit": 1.5, "Points": 300, "Plank Picture": "{{PhysicsArt.GlassPlank}}", "Block Picture": "{{PhysicsArt.GlassBlock}}", "Wedge Picture": "{{PhysicsArt.GlassBlock}}", "Sound": "{{ShatterSound}}", "Hit Sound": "{{ClinkSound}}" },
            "Stone": { "Strength": 40, "Min Hit": 6, "Points": 1000, "Plank Picture": "{{PhysicsArt.StonePlank}}", "Block Picture": "{{PhysicsArt.StoneBlock}}", "Wedge Picture": "{{PhysicsArt.StoneWedge}}", "Sound": "{{CrumbleSound}}", "Hit Sound": "{{ThudSound}}" }
          }
        }
        """;

    // A grub to knock out: round and light, with health for three knocks' worth (flashing as it's hurt), popping in a puff of
    // green as that runs out, worth 5,000.
    private static Entity Grub(Vector2 floor)
    {
        var grub = new Entity(TargetName);
        grub.Transform.Position = new Vector3(floor.X, floor.Y + 0.45f, 0);
        var sprite = grub.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (PhysicsArt.Grub, new Vector2(0.95f), 3);
        grub.AddComponent<CircleCollider>().Radius = 0.45f;
        var body = grub.AddComponent<Body>();
        (body.Rotates, body.Mass, body.Asleep) = (true, 1, true);
        var health = grub.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime, health.HurtSound, health.DeathSound) = (3, 3, 0, KnockSound, PopSound);
        var breakable = grub.AddComponent<Breakable>();
        (breakable.Strength, breakable.MinHit, breakable.Split, breakable.Dust, breakable.Points, breakable.BreakMessage, breakable.ShowPoints) =
            (0, 1.5f, 0, 28, 5000, "Score", true);
        breakable.PointsColor = Color.FromBytes(0xB8, 0xF0, 0x8C);
        return grub;
    }

    // A heavy stone ball hanging on a rope from a beam: it falls when the beam goes.
    private static Entity Weight(Vector2 at, string beam)
    {
        var weight = new Entity("Weight");
        weight.Transform.Position = new Vector3(at, 0);
        var sprite = weight.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (PhysicsArt.Boulder, new Vector2(1.1f), 2);
        weight.AddComponent<CircleCollider>().Radius = 0.55f;
        var body = weight.AddComponent<Body>();
        (body.Rotates, body.Mass) = (true, 5);
        var rope = weight.AddComponent<Rope>();
        (rope.To, rope.Anchor, rope.OtherAnchor, rope.Climbable) = (beam, new Vector2(0, 0.5f), new Vector2(0, -Thick / 2), false);
        return weight;
    }

    // A bird: round, rolling as it lands, heavy enough to knock wood about.
    private static Entity Bird(Vector2 at)
    {
        var bird = new Entity("Bird");
        bird.Transform.Position = new Vector3(at, 0);
        var sprite = bird.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (PhysicsArt.Bird, new Vector2(0.85f), 4);
        bird.AddComponent<CircleCollider>().Radius = 0.4f;
        var body = bird.AddComponent<Body>();
        (body.Rotates, body.Mass, body.Bounciness) = (true, 3, 0.15f);
        return bird;
    }

    // A boulder: bigger and much heavier than a bird, for stone.
    private static Entity Boulder(Vector2 at)
    {
        var boulder = new Entity("Boulder");
        boulder.Transform.Position = new Vector3(at, 0);
        var sprite = boulder.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (PhysicsArt.Boulder, new Vector2(1.05f), 4);
        boulder.AddComponent<CircleCollider>().Radius = 0.5f;
        var body = boulder.AddComponent<Body>();
        (body.Rotates, body.Mass, body.Bounciness) = (true, 8, 0.05f);
        return boulder;
    }

    // Along the top: the level, the shots and grubs left, the score and the best, and a button to try again; the keys at
    // the bottom.
    private static void AddHud(Scene scene, int index, LevelData data)
    {
        var name = Corner(Label("Level Name", Vector2.Zero, $"{index + 1}. {data.Name}", 0.65f, Ink), Anchor.TopLeft, new Vector2(0.6f, -0.7f), TextAlign.Left);
        scene.Add(name);

        var shots = Corner(Label("Shots Left", Vector2.Zero, "Shots: {0}", 0.45f, Ink), Anchor.TopLeft, new Vector2(0.6f, -1.45f), TextAlign.Left);
        (shots.GetComponent<Text>()!.Track, shots.GetComponent<Text>()!.Output) = ("Slingshot", "Shots Left");
        scene.Add(shots);

        var grubs = Corner(Label("Grubs Left", Vector2.Zero, "Grubs: {0} / {1}", 0.45f, Ink), Anchor.TopLeft, new Vector2(0.6f, -2.05f), TextAlign.Left);
        (grubs.GetComponent<Text>()!.Track, grubs.GetComponent<Text>()!.Output) = ("Level", "Targets Left");
        scene.Add(grubs);

        var score = Corner(Label("Score", Vector2.Zero, "0", 0.8f, Ink), Anchor.TopRight, new Vector2(-0.6f, -0.75f), TextAlign.Right);
        var counter = score.AddComponent<Counter>();
        (counter.Message, counter.Format) = ("Score", "{0:N0}");
        scene.Add(score);

        var best = Corner(Label("Best", Vector2.Zero, "Best: {0:N0}", 0.4f, Ink), Anchor.TopRight, new Vector2(-0.6f, -1.5f), TextAlign.Right);
        (best.GetComponent<Text>()!.Track, best.GetComponent<Text>()!.Output) = ("Level", "Best Score");
        scene.Add(best);

        // R tries again; never picked by the arrows (they pull the slingshot) nor clicked by Space or A (they let go).
        var retry = MenuButton("Retry", Vector2.Zero, ButtonAction.RestartScene, null, Key.R, Gray);
        var size = new Vector2(2.4f, 0.8f);
        retry.GetComponent<Sprite>()!.Size = size;
        var button = retry.GetComponent<Button>()!;
        (button.Size, button.Navigable) = (size, false);
        (retry.GetComponent<Text>()!.Value, retry.GetComponent<Text>()!.Size) = ("Retry  R", 0.4f);
        var corner = retry.AddComponent<ScreenAnchor>();
        (corner.Anchor, corner.Offset) = (Anchor.TopRight, new Vector2(-1.8f, -2.4f));
        scene.Add(retry);

        // "{Jump}" shows Space on the keyboard and A on a gamepad.
        var hint = Label("Hint", Vector2.Zero, "Drag the bird back and let go  ·  or pull with the arrows and {Jump}  ·  {Pause} for the menu", 0.4f, Ink);
        var bottom = hint.AddComponent<ScreenAnchor>();
        (bottom.Anchor, bottom.Offset) = (Anchor.Bottom, new Vector2(0, 0.5f));
        scene.Add(hint);
    }

    private static Entity Corner(Entity label, Anchor anchor, Vector2 offset, TextAlign align)
    {
        var text = label.GetComponent<Text>()!;
        (text.Align, text.Order) = (align, 150);
        var corner = label.AddComponent<ScreenAnchor>();
        (corner.Anchor, corner.Offset) = (anchor, offset);
        return label;
    }

    // Opens once the level's won: the stars it earned popping in one by one, the score and the best, and on to the next
    // level (or back to the menu after the last), again, or the menu.
    private static Entity CompletePanel(int index)
    {
        var last = index == Levels.Count - 1;
        var panel = PanelBackdrop("Complete", "Level Complete", 7);
        WithChild(panel, PanelLabel("Heading", new Vector2(0, 2.6f), last ? "All Levels Complete!" : "Level Complete!", 1.1f, Gold));

        var stars = new Entity("Stars");
        stars.Transform.Position = new Vector3(0, 1.2f, 0);
        var row = stars.AddComponent<Stars>();
        (row.Track, row.Output, row.Size, row.Spacing, row.Delay, row.Order) = ("Level", "Stars", 1.3f, 0.3f, 0.45f, 405);
        WithChild(panel, stars);

        var score = PanelLabel("Final Score", new Vector2(0, -0.1f), "Score: {0:N0}", 0.6f, Light);
        (score.GetComponent<Text>()!.Track, score.GetComponent<Text>()!.Output) = ("Score", "Count");
        WithChild(panel, score);
        var best = PanelLabel("Best Score", new Vector2(0, -0.8f), "Best: {0:N0}", 0.42f, Light with { A = 0.75f });
        (best.GetComponent<Text>()!.Track, best.GetComponent<Text>()!.Output) = ("Level", "Best Score");
        WithChild(panel, best);

        var next = last
            ? MenuButton("Menu", new Vector2(-2.6f, -2.2f), ButtonAction.LoadScene, "Main", Key.Enter, Green, order: 410)
            : MenuButton("Next", new Vector2(-2.6f, -2.2f), ButtonAction.LoadScene, LevelScene(index + 1), Key.Enter, Green, order: 410);
        Fit(next, 2.4f);
        WithChild(panel, next);
        WithChild(panel, Fit(MenuButton("Again", new Vector2(0, -2.2f), ButtonAction.RestartScene, null, Key.R, Blue, order: 410), 2.4f));
        if (!last)
            WithChild(panel, Fit(MenuButton("Levels", new Vector2(2.6f, -2.2f), ButtonAction.LoadScene, "Main", Key.Escape, Gray, order: 410), 2.4f));
        else
            WithChild(panel, Fit(MenuButton("Levels", new Vector2(2.6f, -2.2f), ButtonAction.LoadScene, "Main", Key.Escape, Gray, order: 410), 2.4f));
        return panel;
    }

    // Opens once the birds have run out with grubs still standing: how many, and again or the menu.
    private static Entity FailedPanel()
    {
        var panel = PanelBackdrop("Failed", "Level Failed", 5.4f);
        WithChild(panel, PanelLabel("Heading", new Vector2(0, 1.7f), "Level Failed", 1.1f, Red));
        var left = PanelLabel("Detail", new Vector2(0, 0.4f), "Grubs still standing: {0}", 0.5f, Light);
        (left.GetComponent<Text>()!.Track, left.GetComponent<Text>()!.Output) = ("Level", "Targets Left");
        WithChild(panel, left);
        WithChild(panel, Fit(MenuButton("Try Again", new Vector2(-1.7f, -1.3f), ButtonAction.RestartScene, null, Key.Enter, Blue, order: 410), 3));
        WithChild(panel, Fit(MenuButton("Levels", new Vector2(1.7f, -1.3f), ButtonAction.LoadScene, "Main", Key.Escape, Gray, order: 410), 3));
        return panel;
    }

    private static Entity PanelBackdrop(string name, string message, float height)
    {
        var panel = new Entity(name);
        panel.Transform.Position = new Vector3(0, 0, 2);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var shown = panel.AddComponent<Scenes.Panel>();
        (shown.OpenMessage, shown.CloseOnBack, shown.PauseGame) = (message, false, false);
        var backdrop = panel.AddComponent<Sprite>();
        (backdrop.Size, backdrop.Tint, backdrop.Order) = (new Vector2(9, height), Panel, 400);
        return panel;
    }

    private static Entity PanelLabel(string name, Vector2 at, string value, float size, Color color)
    {
        var label = Label(name, at, value, size, color);
        label.GetComponent<Text>()!.Order = 401;
        return label;
    }

    private static Entity Fit(Entity button, float width)
    {
        var size = new Vector2(width, 1);
        button.GetComponent<Sprite>()!.Size = size;
        button.GetComponent<Button>()!.Size = size;
        button.GetComponent<Text>()!.Size = 0.5f;
        return button;
    }

    // ----- The menu: a card for each level, with its best stars and score, each opening once the one before is won

    private static Scene Menu()
    {
        var scene = new Scene();
        AddCamera(scene, follow: false);
        scene.Entities.Single(e => e.Name == "Main Camera").Transform.Position = new Vector3(0, 5, 0);
        AddScenery(scene);

        scene.Add(Label("Title", new Vector2(0, 10.2f), "PHYSICS", 1.7f, Red));
        scene.Add(Label("Subtitle", new Vector2(0, 9), "Knock out every grub, with as few birds as you can", 0.5f, Ink));

        for (var i = 0; i < Levels.Count; i++)
        {
            var x = (i - (Levels.Count - 1) / 2f) * 5.6f;
            var card = MenuButton($"Level {i + 1}", new Vector2(x, 5.6f), ButtonAction.LoadScene, LevelScene(i), i == 0 ? Key.Enter : Key.None, i == 0 ? Blue : Color.FromBytes(0x3A, 0x6E, 0xB8));
            var size = new Vector2(4.8f, 3.6f);
            card.GetComponent<Sprite>()!.Size = size;
            var button = card.GetComponent<Button>()!;
            button.Size = size;
            if (i > 0)
                button.NeedsSaved = SaveKey(i - 1) + ".Stars";
            card.RemoveComponent(card.GetComponent<Text>()!);

            // The level's name at the top of the card, its best stars under it, its best score at the bottom.
            var title = Label("Name", new Vector2(0, 0.85f), $"{i + 1}. {Levels[i].Name}", 0.55f, Light);
            title.GetComponent<Text>()!.Order = 105;
            WithChild(card, title);

            var stars = new Entity("Stars");
            stars.Transform.Position = new Vector3(0, -0.55f, 0);
            var row = stars.AddComponent<Stars>();
            (row.Saved, row.Size, row.Order) = (SaveKey(i) + ".Stars", 0.8f, 105);
            WithChild(card, stars);

            var best = Label("Best", new Vector2(0, -1.35f), "", 0.36f, Light);
            var counter = best.AddComponent<Counter>();
            (counter.Message, counter.SaveBestAs, counter.Format, counter.Pop) = (null, SaveKey(i) + ".Best", "{1:'Best: '#,##0;;'Not won yet'}", 1);
            best.GetComponent<Text>()!.Order = 105;
            WithChild(card, best);
            scene.Add(card);
        }

        scene.Add(MenuButton("Quit", new Vector2(0, 2), ButtonAction.Quit, null, Key.Escape, Gray));

        // A bird and a grub, waiting.
        var bird = Bird(new Vector2(-9, 0.4f));
        bird.RemoveComponent(bird.GetComponent<Body>()!);
        scene.Add(bird);
        var grub = Grub(new Vector2(9, 0));
        grub.RemoveComponent(grub.GetComponent<Breakable>()!);
        grub.RemoveComponent(grub.GetComponent<Health>()!);
        grub.RemoveComponent(grub.GetComponent<Body>()!);
        scene.Add(grub);

        var hint = Label("Hint", Vector2.Zero, "Arrows to choose  ·  Enter to play  ·  Win a level to open the next", 0.4f, Ink);
        var anchor = hint.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.Bottom, new Vector2(0, 0.5f));
        scene.Add(hint);
        return scene;
    }
}
