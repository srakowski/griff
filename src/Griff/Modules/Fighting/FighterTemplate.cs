using System.Numerics;
using Griff.Audio;
using Griff.Input;
using Griff.Projects;
using Griff.Scenes;
using Griff.Templates;
using static Griff.Templates.ProjectTemplates;

namespace Griff.Modules.Fighting;

// The Fighter template, "Dojo Clash": a one-on-one fighting game with no code, for two people at one machine (sharing the
// keyboard, on two controllers, or one of each) or one against the computer, with a training mode. A title screen, a
// character select for each (two players' cursors, or one against the computer, or one to train), and the dojo at dusk the
// fight is on: best of three rounds against the clock, health bars, rounds won, "Round 1", "Fight!", "K.O.", the winner,
// a rematch, and a pause menu. Kenta and Mira each have their own painted sheet, moves (quarter circles, a dragon punch,
// charges, an overhead) and hitboxes; music and every hit, block and announcement sound are made in code.
internal sealed class FighterTemplate : ProjectTemplate
{
    public override string Title => "Fighter";

    public override string Description => "Dojo Clash: a one-on-one fighting game for two at one keyboard, on two controllers or one of each, or against the computer, with no code. Pick Kenta or Mira, then fight best of three rounds against the clock: quarter circles, a dragon punch, charges and an overhead, blocking high and low, and combos. Training has a dummy that stands, crouches, jumps or fights back. Left keys: WASD, F punch, G kick; right keys: the arrows, K punch, L kick; or a controller each (X punch, A kick).";

    public override int Order => 126;

    protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Tense, seed: 31) with { Volume = 0.45f, Tempo = 138 };

    protected override Scene BuildStartScene() => TitleScreen();

    protected override Dictionary<string, Scene> BuildMoreScenes() => new()
    {
        [$"Scenes/{VersusSelect}.scene"] = SelectScene(players: 2, computer: false, next: StageScene, heading: "Versus: choose your fighters"),
        [$"Scenes/{SoloSelect}.scene"] = SelectScene(players: 1, computer: true, next: StageScene, heading: "Against the computer: choose your fighter"),
        [$"Scenes/{TrainingSelect}.scene"] = SelectScene(players: 1, computer: true, next: TrainingScene, heading: "Training: choose your fighter"),
        [$"Scenes/{StageScene}.scene"] = Stage(training: false),
        [$"Scenes/{TrainingScene}.scene"] = Stage(training: true),
    };

    protected override Dictionary<string, Entity> BuildPrefabs() => Roster.ToDictionary(c => PrefabPath(c.Name), c => Fighter(c, Vector2.Zero));

    public override IReadOnlyDictionary<string, byte[]> Images() => FightingArt.VersusFiles();

    // Its own sounds: hits, blocks, a grunt, a whoosh for a special, the K.O., the bell, "Fight!" and a pick.
    protected override IReadOnlyDictionary<string, SoundRecipe> MoreSounds => new Dictionary<string, SoundRecipe>
    {
        [PunchSound] = new() { Wave = Waveform.Noise, Frequency = 0.3f, Slide = -0.2f, Sustain = 0.02f, Decay = 0.12f, Punch = 0.6f, LowPassCutoff = 0.6f, Volume = 0.55f },
        [BlockSound] = new() { Wave = Waveform.Square, SquareDuty = 0.35f, Frequency = 0.62f, Slide = -0.1f, Sustain = 0.02f, Decay = 0.1f, Punch = 0.3f, HighPassCutoff = 0.2f, Volume = 0.35f },
        [GruntSound] = new() { Wave = Waveform.Sawtooth, Frequency = 0.16f, Slide = -0.08f, VibratoDepth = 0.2f, VibratoSpeed = 0.5f, Sustain = 0.05f, Decay = 0.12f, LowPassCutoff = 0.35f, Volume = 0.3f },
        [WhooshSound] = new() { Wave = Waveform.Noise, Frequency = 0.5f, Slide = 0.15f, Attack = 0.05f, Sustain = 0.06f, Decay = 0.16f, HighPassCutoff = 0.3f, Volume = 0.3f },
        [KoSound] = new() { Wave = Waveform.Noise, Frequency = 0.08f, Slide = -0.04f, Sustain = 0.12f, Decay = 0.6f, Punch = 0.7f, LowPassCutoff = 0.4f, Volume = 0.7f },
        [BellSound] = new() { Wave = Waveform.Triangle, Frequency = 0.52f, VibratoDepth = 0.2f, VibratoSpeed = 0.7f, Sustain = 0.1f, Decay = 0.6f, Punch = 0.4f, Volume = 0.45f },
        [FightSound] = new() { Wave = Waveform.Square, SquareDuty = 0.3f, Frequency = 0.36f, Slide = 0.25f, ChangeAmount = 0.35f, ChangeSpeed = 0.45f, Sustain = 0.12f, Decay = 0.25f, Punch = 0.5f, Volume = 0.45f },
        [PickSound] = new() { Wave = Waveform.Square, SquareDuty = 0.25f, Frequency = 0.48f, ChangeAmount = 0.4f, ChangeSpeed = 0.6f, Sustain = 0.06f, Decay = 0.2f, Punch = 0.3f, Volume = 0.4f },
    };

    public const string PunchSound = "Sounds/punch.wav";
    public const string BlockSound = "Sounds/block.wav";
    public const string GruntSound = "Sounds/grunt.wav";
    public const string WhooshSound = "Sounds/whoosh.wav";
    public const string KoSound = "Sounds/ko.wav";
    public const string BellSound = "Sounds/bell.wav";
    public const string FightSound = "Sounds/fight.wav";
    public const string PickSound = "Sounds/pick.wav";

    // Hits smack, blocks clack, fighters grunt and go down hard; the bell rings each round in and every K.O.
    protected internal override void WireSounds(Entity entity)
    {
        foreach (var component in entity.Components)
        {
            switch (component)
            {
                case Hitboxes boxes:
                    TemplateSounds.Set(boxes.HitSound, s => boxes.HitSound = s, PunchSound);
                    break;
                case FightingCharacter fighter:
                    TemplateSounds.Set(fighter.BlockSound, s => fighter.BlockSound = s, BlockSound);
                    break;
                case Health health when entity.GetComponent<FightingCharacter>() is not null:
                    TemplateSounds.Set(health.HurtSound, s => health.HurtSound = s, GruntSound);
                    TemplateSounds.Set(health.DeathSound, s => health.DeathSound = s, KoSound);
                    break;
                case VersusMatch match:
                    TemplateSounds.Set(match.RoundSound, s => match.RoundSound = s, BellSound);
                    TemplateSounds.Set(match.FightSound, s => match.FightSound = s, FightSound);
                    TemplateSounds.Set(match.KoSound, s => match.KoSound = s, BellSound);
                    TemplateSounds.Set(match.WinSound, s => match.WinSound = s, TemplateSounds.PowerUp);
                    break;
                case CharacterSelect select:
                    TemplateSounds.Set(select.MoveSound, s => select.MoveSound = s, TemplateSounds.Select);
                    TemplateSounds.Set(select.PickSound, s => select.PickSound = s, PickSound);
                    break;
            }
        }

        base.WireSounds(entity);
    }

    // ----- The fighters -----

    /// <summary>
    /// A character to pick: its name (its prefab's), sheet and clips, moves, boxes (its hurtboxes on every frame and each
    /// move's hitbox), and how fast it walks, how high it jumps and how much it can take.
    /// </summary>
    public sealed record Character(string Name, string Image, string Clips, string[] Moves, IReadOnlyList<FrameBox> Boxes,
        float WalkSpeed, float BackWalkSpeed, float JumpHeight, float Health);

    /// <summary>The scenes: the title, the three character selects (versus, against the computer, training), the fight and training.</summary>
    public const string VersusSelect = "Select", SoloSelect = "Solo", TrainingSelect = "Practice", StageScene = "Stage", TrainingScene = "Training";

    /// <summary>The two fighters in a fight: the first player's on the left, the second's (or the computer's) on the right.</summary>
    public const string FirstFighter = "Player 1", SecondFighter = "Player 2";

    /// <summary>The entity the match is on, and the one the character select is on.</summary>
    public const string MatchName = "Match", SelectName = "Select";

    /// <summary>Where each character's prefab is kept: "Prefabs/Kenta.prefab".</summary>
    public static string PrefabPath(string character) => $"Prefabs/{character}.prefab";

    /// <summary>The characters, in the order of the select screen's cards.</summary>
    public static IReadOnlyList<Character> Roster { get; } =
    [
        new("Kenta", FightingArt.Kenta, FightingArt.KentaClips,
        [
            "Jab: 5P frames 3/2/7 cancels special",
            "Kick: 5K frames 6/3/14 cancels special",
            "Low Kick: 2K frames 5/3/14 low cancels special",
            "Air Kick: j.K clip Kick frames 3/10/2",
            $"Palm: 236P frames 10/4/20 chip 0.25 sound {WhooshSound}",
            $"Uppercut: 623P frames 3/10/26 push 1,8 sound {WhooshSound}",
            $"Rush: 214K clip Palm frames 8/8/18 push 7,0 sound {WhooshSound}",
        ],
        Boxes("Palm", "Uppercut", risesOn: "Uppercut",
        [
            FrameBox.Hitbox("Jab", 1, new Vector2(0.58f, 0.25f), new Vector2(0.32f, 0.26f), damage: 3, knockback: new Vector2(2, 0), stun: 14),
            FrameBox.Hitbox("Kick", 1, new Vector2(0.6f, -0.19f), new Vector2(0.42f, 0.3f), damage: 4, knockback: new Vector2(3, 0), stun: 18),
            FrameBox.Hitbox("LowKick", 1, new Vector2(0.6f, -0.86f), new Vector2(0.46f, 0.26f), damage: 3, knockback: new Vector2(2, 0), stun: 16),
            FrameBox.Hitbox("Palm", 1, new Vector2(0.56f, 0.04f), new Vector2(0.42f, 0.5f), damage: 6, knockback: new Vector2(4, 1), stun: 22),
            FrameBox.Hitbox("Uppercut", 1, new Vector2(0.3f, 0.6f), new Vector2(0.36f, 0.6f), damage: 7, knockback: new Vector2(2, 6), stun: 30),
            FrameBox.Hitbox("Uppercut", 2, new Vector2(0.2f, 0.75f), new Vector2(0.36f, 0.6f), damage: 7, knockback: new Vector2(2, 6), stun: 30),
        ]),
        WalkSpeed: 3, BackWalkSpeed: 2.4f, JumpHeight: 2.6f, Health: 60),

        new("Mira", FightingArt.Mira, FightingArt.MiraClips,
        [
            "Jab: 5P frames 2/2/6 cancels special",
            "Kick: 5K frames 5/3/13 cancels special",
            "Low Kick: 2K frames 4/3/13 low cancels special",
            "Axe Kick: 6K clip HighKick frames 16/3/14 high",
            "Air Kick: j.K clip Kick frames 3/10/2",
            $"Spin Kick: [4]6K clip HighKick frames 7/6/18 push 6,0 sound {WhooshSound}",
            $"Flash Kick: [2]8K clip FlashKick frames 3/9/28 push 0.5,9 sound {WhooshSound}",
        ],
        Boxes("HighKick", "FlashKick", risesOn: "FlashKick",
        [
            FrameBox.Hitbox("Jab", 1, new Vector2(0.56f, 0.25f), new Vector2(0.3f, 0.24f), damage: 2, knockback: new Vector2(2, 0), stun: 13),
            FrameBox.Hitbox("Kick", 1, new Vector2(0.62f, -0.19f), new Vector2(0.44f, 0.3f), damage: 4, knockback: new Vector2(3, 0), stun: 18),
            FrameBox.Hitbox("LowKick", 1, new Vector2(0.62f, -0.86f), new Vector2(0.48f, 0.26f), damage: 3, knockback: new Vector2(2, 0), stun: 16),
            FrameBox.Hitbox("HighKick", 1, new Vector2(0.64f, 0.42f), new Vector2(0.46f, 0.3f), damage: 5, knockback: new Vector2(4, 1), stun: 20),
            FrameBox.Hitbox("FlashKick", 1, new Vector2(0.28f, 0.7f), new Vector2(0.4f, 0.7f), damage: 7, knockback: new Vector2(2, 7), stun: 30),
            FrameBox.Hitbox("FlashKick", 2, new Vector2(0.36f, 0.7f), new Vector2(0.4f, 0.6f), damage: 7, knockback: new Vector2(2, 7), stun: 30),
        ]),
        WalkSpeed: 3.4f, BackWalkSpeed: 2.8f, JumpHeight: 2.9f, Health: 54),
    ];

    // A fighter's boxes: hurtboxes on its body and head on every frame but the K.O. (lower crouching, its first special
    // frame crouched for the rising one), and its moves' hitboxes, with the arm or leg out there hurtable too.
    private static List<FrameBox> Boxes(string special, string rising, string risesOn, IReadOnlyList<FrameBox> hits)
    {
        var boxes = new List<FrameBox>();
        void Standing(string clip, int frame, float lean = 0)
        {
            boxes.Add(FrameBox.Hurtbox(clip, frame, new Vector2(0, -0.38f), new Vector2(0.5f, 1.25f)));
            boxes.Add(FrameBox.Hurtbox(clip, frame, new Vector2(lean / 16, 0.56f), Vector2.Zero, multiplier: 1.5f).AsCircle(0.28f));
        }

        void Crouched(string clip, int frame)
        {
            boxes.Add(FrameBox.Hurtbox(clip, frame, new Vector2(0, -0.6f), new Vector2(0.55f, 0.8f)));
            boxes.Add(FrameBox.Hurtbox(clip, frame, new Vector2(0, 0.13f), Vector2.Zero, multiplier: 1.5f).AsCircle(0.28f));
        }

        Standing("Idle", 0);
        Standing("Idle", 1);
        Standing("Walk", 0);
        Standing("Walk", 1);
        Crouched("Crouch", 0);
        Standing("Jump", 0);
        Standing("Block", 0, -1);
        Crouched("CrouchBlock", 0);
        Standing("Hurt", 0, -2);
        for (var frame = 0; frame < 3; frame++)
        {
            Standing("Jab", frame);
            Standing("Kick", frame);
            Crouched("LowKick", frame);
            Standing(special, frame);
            if (frame == 0 && rising == risesOn)
                Crouched(rising, frame);
            else
                Standing(rising, frame);
        }

        boxes.Add(FrameBox.Hurtbox("Jab", 1, new Vector2(0.3f, 0.25f), new Vector2(0.3f, 0.2f)));
        boxes.Add(FrameBox.Hurtbox("Kick", 1, new Vector2(0.32f, -0.3f), new Vector2(0.4f, 0.25f)));
        boxes.Add(FrameBox.Hurtbox("LowKick", 1, new Vector2(0.32f, -0.8f), new Vector2(0.4f, 0.2f)));
        boxes.AddRange(hits);
        return boxes;
    }

    // A fighter as its prefab has it: its sheet, clips, a body and collider, health that doesn't run out between hits (each
    // lands), its boxes and its moves.
    private static Entity Fighter(Character character, Vector2 at)
    {
        var entity = new Entity(character.Name);
        entity.Transform.Position = new Vector3(at, 0);
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Columns, sprite.Rows, sprite.Order) =
            (character.Image, new Vector2(1.5f, 2), FightingArt.VersusColumns, FightingArt.VersusRows, 10);
        var animation = entity.AddComponent<Animation>();
        (animation.Clips, animation.Clip) = (character.Clips, "Idle");
        entity.AddComponent<BoxCollider>().Size = new Vector2(0.5f, 2);
        entity.AddComponent<Body>();
        var health = entity.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime, health.DestroyOnDeath) = (character.Health, character.Health, 0, false);
        var boxes = entity.AddComponent<Hitboxes>();
        (boxes.Boxes, boxes.HitStop) = (FrameBox.FormatAll(character.Boxes), 0.06f);
        var fighter = entity.AddComponent<FightingCharacter>();
        (fighter.Moves, fighter.WalkSpeed, fighter.BackWalkSpeed, fighter.JumpHeight) =
            (character.Moves, character.WalkSpeed, character.BackWalkSpeed, character.JumpHeight);
        return entity;
    }

    // ----- Colors -----

    private static readonly Color Gold = Color.FromBytes(0xF8, 0xCC, 0x3A);
    private static readonly Color Light = Color.FromBytes(0xFA, 0xF4, 0xE8);
    private static readonly Color Dim = Color.FromBytes(0xC8, 0xC0, 0xD8);
    private static readonly Color Red = Color.FromBytes(0xE0, 0x44, 0x3C);
    private static readonly Color Blue = Color.FromBytes(0x3C, 0x8C, 0xE8);
    private static readonly Color Gray = Color.FromBytes(0x3E, 0x42, 0x52);
    private static readonly Color Shade = new(0.06f, 0.05f, 0.1f, 0.78f);
    private static readonly Color Ink = new(0.05f, 0.04f, 0.08f, 1);

    // ----- The title screen -----

    private static Scene TitleScreen()
    {
        var scene = new Scene();
        AddCamera(scene);
        AddBackdrop(scene, FightingArt.Dojo);
        var band = new Entity("Band");
        band.Transform.Position = new Vector3(0, 0.2f, 0);
        var shade = band.AddComponent<Sprite>();
        (shade.Size, shade.Tint, shade.Order) = (new Vector2(30, 6.4f), Shade, -5);
        scene.Add(band);

        scene.Add(Label("Title", new Vector2(0, 2.4f), "DOJO CLASH", 1.5f, Gold));
        scene.Add(Label("Subtitle", new Vector2(0, 1.45f), "One on one, best of three", 0.45f, Dim));
        scene.Add(Showing(Roster[0], new Vector2(-5, -1.6f), left: false));
        scene.Add(Showing(Roster[1], new Vector2(5, -1.6f), left: true));

        scene.Add(Fit(MenuButton("Versus", new Vector2(0, 0.35f), ButtonAction.LoadScene, VersusSelect, Key.Enter, Red), 4.6f));
        scene.Add(Fit(MenuButton("Vs Computer", new Vector2(0, -0.65f), ButtonAction.LoadScene, SoloSelect, Key.None, Blue), 4.6f));
        scene.Add(Fit(MenuButton("Training", new Vector2(0, -1.65f), ButtonAction.LoadScene, TrainingSelect, Key.None, Gray), 4.6f));
        scene.Add(Fit(MenuButton("Quit", new Vector2(0, -2.65f), ButtonAction.Quit, null, Key.Escape, Gray), 4.6f));

        scene.Add(Hint("Arrows to choose  ·  Enter to pick  ·  Two players: WASD with F and G, the arrows with K and L, or a controller each"));
        return scene;
    }

    // A fighter standing for show, breathing.
    private static Entity Showing(Character character, Vector2 at, bool left)
    {
        var entity = new Entity(character.Name);
        entity.Transform.Position = new Vector3(at, 0);
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Columns, sprite.Rows, sprite.FlipX, sprite.Order) =
            (character.Image, new Vector2(2.25f, 3), FightingArt.VersusColumns, FightingArt.VersusRows, left, 10);
        var animation = entity.AddComponent<Animation>();
        (animation.Clips, animation.Clip) = (character.Clips, "Idle");
        return entity;
    }

    // ----- Character select -----

    private static Scene SelectScene(int players, bool computer, string next, string heading)
    {
        var scene = new Scene();
        AddCamera(scene);
        AddBackdrop(scene, FightingArt.SelectBackdrop);
        scene.Add(Label("Heading", new Vector2(0, 3.3f), heading, 0.6f, Light));

        // The cards, side by side in the middle, with the cursors over them.
        var select = new Entity(SelectName);
        select.Transform.Position = new Vector3(0, 0.2f, 0);
        var pick = select.AddComponent<CharacterSelect>();
        (pick.Characters, pick.Players, pick.Computer, pick.Scene) = (string.Join(", ", Roster.Select(c => c.Name)), players, computer, next);
        (pick.FirstIndex, pick.SecondIndex) = (0, Math.Min(1, Roster.Count - 1));
        scene.Add(select);
        for (var i = 0; i < Roster.Count; i++)
        {
            var character = Roster[i];
            var card = new Entity(character.Name);
            card.Transform.Position = new Vector3((i - (Roster.Count - 1) / 2f) * 2.6f, 0, 0);
            var panel = card.AddComponent<Sprite>();
            (panel.Size, panel.Tint, panel.Order) = (new Vector2(2.2f, 2.7f), Shade, 20);
            var picture = new Entity("Picture");
            picture.Transform.Position = new Vector3(0, 0.15f, 0);
            var face = picture.AddComponent<Sprite>();
            (face.Image, face.Size, face.Columns, face.Rows, face.Order) = (character.Image, new Vector2(1.5f, 2), FightingArt.VersusColumns, FightingArt.VersusRows, 21);
            var name = Label("Name", new Vector2(0, -1.05f), character.Name, 0.36f, Light);
            name.GetComponent<Text>()!.Order = 22;
            scene.Add(card, select);
            scene.Add(picture, card);
            scene.Add(name, card);
        }

        scene.Add(Cursor(pick.FirstCursor!, Red, new Vector2(2.4f, 2.9f), 30));
        scene.Add(Cursor(pick.SecondCursor!, Blue, new Vector2(2.6f, 3.1f), 29));

        AddSide(scene, pick.FirstPreview!, -5.2f, "P1", "First", Red, flip: false);
        AddSide(scene, pick.SecondPreview!, 5.2f, players >= 2 ? "P2" : "CPU", "Second", Blue, flip: true);

        var back = Fit(MenuButton("Back", Vector2.Zero, ButtonAction.LoadScene, Project.DefaultSceneName, Key.Escape, Gray), 2.4f);
        back.GetComponent<Button>()!.Navigable = false;
        (back.GetComponent<Text>()!.Value, back.GetComponent<Text>()!.Size) = ("Back  Esc", 0.36f);
        var corner = back.AddComponent<ScreenAnchor>();
        (corner.Anchor, corner.Offset) = (Anchor.TopLeft, new Vector2(1.5f, -0.6f));
        scene.Add(back);

        if (players >= 2)
        {
            // The one at the keyboard and the one on a controller change places.
            var swap = Fit(MenuButton("Swap Controls", Vector2.Zero, ButtonAction.Do, null, Key.Tab, Gray), 3.6f);
            var button = swap.GetComponent<Button>()!;
            (button.Navigable, button.Do, button.Targets) = (false, nameof(CharacterSelect.SwapControls), SelectName);
            (swap.GetComponent<Text>()!.Value, swap.GetComponent<Text>()!.Size) = ("Swap Controls  Tab", 0.36f);
            var at = swap.AddComponent<ScreenAnchor>();
            (at.Anchor, at.Offset) = (Anchor.TopRight, new Vector2(-2.1f, -0.6f));
            scene.Add(swap);
        }

        scene.Add(Hint(players >= 2
            ? "Each player: left and right to choose, punch to pick, dash to change your mind  ·  WASD with F, the arrows with K, or a controller's X"
            : "Left and right to choose  ·  punch (J, or a controller's X) or Enter to pick  ·  or click a fighter"));
        return scene;
    }

    private static Entity Cursor(string name, Color color, Vector2 size, int order)
    {
        var cursor = new Entity(name);
        cursor.Transform.Position = new Vector3(0, 0.2f, 0);
        var sprite = cursor.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Tint, sprite.Order) = (FightingArt.Cursor, size, color, order);
        return cursor;
    }

    // A side of the select screen: the player's fighter big, its name, whether they're ready and what they play with.
    private static void AddSide(Scene scene, string preview, float x, string tag, string side, Color color, bool flip)
    {
        var big = new Entity(preview);
        big.Transform.Position = new Vector3(x, -0.4f, 0);
        var sprite = big.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Columns, sprite.Rows, sprite.FlipX, sprite.Order) =
            (FightingArt.Kenta, new Vector2(3, 4), FightingArt.VersusColumns, FightingArt.VersusRows, flip, 15);
        var animation = big.AddComponent<Animation>();
        (animation.Clips, animation.Clip) = (FightingArt.KentaClips, "Idle");
        scene.Add(big);

        scene.Add(Label($"{tag} Tag", new Vector2(x, 2.5f), tag, 0.7f, color));
        scene.Add(Tracking($"{tag} Name", new Vector2(x, 1.85f), "{0}", 0.5f, Light, SelectName, side));
        scene.Add(Tracking($"{tag} Status", new Vector2(x, -2.6f), "{0}", 0.45f, Gold, SelectName, side + " Status"));
        scene.Add(Tracking($"{tag} Controls", new Vector2(x, -3.1f), "{0}", 0.3f, Dim, SelectName, side + " Controls"));
    }

    // ----- The fight, and training -----

    private static Scene Stage(bool training)
    {
        var scene = new Scene();
        AddCamera(scene);
        AddBackdrop(scene, FightingArt.Dojo);

        // The floor and the walls at the screen's edges.
        scene.Add(Solid("Floor", new Vector2(0, FloorTop - 1), new Vector2(30, 2)));
        scene.Add(Solid("Left Wall", new Vector2(-WallX - 0.5f, 2), new Vector2(1, 16)));
        scene.Add(Solid("Right Wall", new Vector2(WallX + 0.5f, 2), new Vector2(1, 16)));

        // The fighters: Kenta and Mira until the picks are put in.
        var first = Placed(Roster[0], FirstFighter, -2.4f, player: 1, opponent: SecondFighter);
        var second = Placed(Roster[Math.Min(1, Roster.Count - 1)], SecondFighter, 2.4f, player: 2, opponent: FirstFighter);
        second.GetComponent<Sprite>()!.FlipX = true;
        // Training's dummy is a computer fighter standing still: its buttons name its actions and outputs, so it has them in the scene.
        if (training)
            second.AddComponent<ComputerFighter>().Behavior = ComputerBehavior.Stand;
        scene.Add(first);
        scene.Add(second);

        var match = new Entity(MatchName);
        var versus = match.AddComponent<VersusMatch>();
        (versus.First, versus.Second) = (FirstFighter, SecondFighter);
        if (training)
            (versus.RoundsToWin, versus.RoundTime, versus.Computer, versus.ComputerBehavior) = (0, 0, true, ComputerBehavior.Stand);
        scene.Add(match);

        AddHud(scene, training);
        if (training)
            AddTrainingMenu(scene);
        else
            scene.Add(WinPanel());
        AddPauseMenu(scene, menuScene: Project.DefaultSceneName);
        return scene;
    }

    /// <summary>Where the floor's top is, in world units: the fighters stand on it.</summary>
    public const float FloorTop = -2.6f;

    /// <summary>How far either way the walls are from the middle.</summary>
    public const float WallX = 6.4f;

    private static Entity Placed(Character character, string name, float x, int player, string opponent)
    {
        var fighter = Fighter(character, new Vector2(x, FloorTop + 1));
        fighter.Name = name;
        fighter.Prefab = PrefabPath(character.Name);
        fighter.AddComponent<PlayedBy>().Player = player;
        fighter.GetComponent<FightingCharacter>()!.Opponent = opponent;
        return fighter;
    }

    private static Entity Solid(string name, Vector2 at, Vector2 size)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        entity.AddComponent<BoxCollider>().Size = size;
        return entity;
    }

    // Along the top: each fighter's health and name, the rounds each has won, the clock; what's announced in the middle; the
    // combos either side.
    private static void AddHud(Scene scene, bool training)
    {
        scene.Add(HealthBar("P1 Health", FirstFighter, -3.55f, BarDirection.Left));
        scene.Add(HealthBar("P2 Health", SecondFighter, 3.55f, BarDirection.Right));
        scene.Add(Pinned(Tracking("P1 Name", Vector2.Zero, "{0}", 0.42f, Light, MatchName, "First Name"), Anchor.Top, new Vector2(-6.4f, -1.1f), TextAlign.Left));
        scene.Add(Pinned(Tracking("P2 Name", Vector2.Zero, "{0}", 0.42f, Light, MatchName, "Second Name"), Anchor.Top, new Vector2(6.4f, -1.1f), TextAlign.Right));

        if (!training)
        {
            scene.Add(RoundsWon("P1 Rounds", "First Wins", -1.6f));
            scene.Add(RoundsWon("P2 Rounds", "Second Wins", 1.6f));
            var clockBack = new Entity("Clock Back");
            clockBack.AddComponent<ScreenAnchor>().Anchor = Anchor.Top;
            clockBack.GetComponent<ScreenAnchor>()!.Offset = new Vector2(0, -0.6f);
            var plate = clockBack.AddComponent<Sprite>();
            (plate.Size, plate.Tint, plate.Order) = (new Vector2(1.3f, 0.95f), Shade, 140);
            scene.Add(clockBack);
            scene.Add(Pinned(Tracking("Clock", Vector2.Zero, "{0}", 0.7f, Gold, MatchName, "Time Left"), Anchor.Top, new Vector2(0, -0.6f), TextAlign.Center));
        }

        var announce = Tracking("Announcement", new Vector2(0, 0.9f), "{0}", 1.3f, Gold, MatchName, nameof(VersusMatch.Announcement));
        announce.GetComponent<Text>()!.Order = 250;
        scene.Add(announce);

        // "3 hits", from a combo's second hit on.
        scene.Add(Tracking("P1 Combo", new Vector2(-4.8f, 1.1f), "{0:0' hits';;#}", 0.55f, Red, FirstFighter, "Combo Hits"));
        scene.Add(Tracking("P2 Combo", new Vector2(4.8f, 1.1f), "{0:0' hits';;#}", 0.55f, Blue, SecondFighter, "Combo Hits"));
        if (training)
            scene.Add(Tracking("Combo Damage", new Vector2(-4.8f, 0.55f), "{0:0' damage';;#}", 0.38f, Light, FirstFighter, "Combo Damage"));

        scene.Add(Hint(training
            ? "Kenta: 236P palm  ·  623P uppercut  ·  214K rush        Mira: [4]6K spin kick  ·  [2]8K flash kick  ·  6K axe kick (high)        {Pause} to pause"
            : "Hold back to block, down-back to block low  ·  {Pause} to pause"));
    }

    private static Entity HealthBar(string name, string fighter, float x, BarDirection direction)
    {
        var entity = new Entity(name);
        var anchor = entity.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.Top, new Vector2(x, -0.55f));
        var bar = entity.AddComponent<Bar>();
        (bar.Track, bar.Size, bar.Direction, bar.Order) = (fighter, new Vector2(5.8f, 0.42f), direction, 150);
        (bar.Fill, bar.Background, bar.Trail) = (Gold, new Color(0.35f, 0.05f, 0.08f, 0.85f), new Color(1, 0.4f, 0.3f, 0.9f));
        return entity;
    }

    private static Entity RoundsWon(string name, string output, float x)
    {
        var entity = new Entity(name);
        var anchor = entity.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.Top, new Vector2(x, -1.15f));
        var stars = entity.AddComponent<Stars>();
        (stars.Track, stars.Output, stars.Count, stars.Size, stars.Spacing, stars.Order) = (MatchName, output, 2, 0.4f, 0.1f, 150);
        return entity;
    }

    // Opens once the match is won: who won, and a rematch, the character select or the title.
    private static Entity WinPanel()
    {
        var panel = new Entity("Winner");
        panel.Transform.Position = new Vector3(0, 0, 2);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var shown = panel.AddComponent<Panel>();
        (shown.OpenMessage, shown.CloseOnBack, shown.PauseGame) = ("Match Over", false, false);
        var backdrop = panel.AddComponent<Sprite>();
        (backdrop.Size, backdrop.Tint, backdrop.Order) = (new Vector2(8, 4.6f), new Color(0.08f, 0.06f, 0.12f, 0.92f), 400);
        panel.Transform.Position = new Vector3(0, -0.6f, 2);

        var heading = Tracking("Heading", new Vector2(0, 1.4f), "{0}!", 0.9f, Gold, MatchName, nameof(VersusMatch.Announcement));
        heading.GetComponent<Text>()!.Order = 401;
        WithChild(panel, heading);
        WithChild(panel, Fit(MenuButton("Rematch", new Vector2(0, 0.25f), ButtonAction.RestartScene, null, Key.Enter, Red, order: 410), 4.6f));
        var select = Fit(MenuButton("Character Select", new Vector2(0, -0.85f), ButtonAction.LoadScene, VersusSelect, Key.None, Blue, order: 410), 4.6f);
        select.GetComponent<Button>()!.SavedScene = "Pick.Scene";
        WithChild(panel, select);
        WithChild(panel, Fit(MenuButton("Title", new Vector2(0, -1.95f), ButtonAction.LoadScene, Project.DefaultSceneName, Key.Escape, Gray, order: 410), 4.6f));
        return panel;
    }

    // Training's buttons: the dummy's guard and what it does, the hitboxes shown, and a reset; each on a number key too.
    private static void AddTrainingMenu(Scene scene)
    {
        (string Name, string Label, string Do, string Target, string Track, string Output, Key Key)[] options =
        [
            ("Dummy Block", "1  Block: {0}", nameof(FightingCharacter.NextAutoBlock), SecondFighter, SecondFighter, "Auto Block", Key.D1),
            ("Dummy Action", "2  Dummy: {0}", nameof(ComputerFighter.NextBehavior), SecondFighter, SecondFighter, "Behavior", Key.D2),
            ("Show Hitboxes", "3  Hitboxes: {0}", nameof(VersusMatch.ToggleHitboxes), MatchName, MatchName, "Hitboxes", Key.D3),
            ("Reset", "4  Reset", nameof(VersusMatch.ResetPositions), MatchName, "", "", Key.D4),
        ];
        for (var i = 0; i < options.Length; i++)
        {
            var (name, label, action, target, track, output, key) = options[i];
            var entity = Fit(MenuButton(name, Vector2.Zero, ButtonAction.Do, null, key, Gray), 3.4f);
            var button = entity.GetComponent<Button>()!;
            (button.Navigable, button.Do, button.Targets, button.Fade) = (false, action, target, 0);
            var text = entity.GetComponent<Text>()!;
            (text.Value, text.Size) = (label, 0.32f);
            if (track.Length > 0)
                (text.Track, text.Output) = (track, output);
            entity.GetComponent<Sprite>()!.Size = button.Size = new Vector2(3.4f, 0.6f);
            var anchor = entity.AddComponent<ScreenAnchor>();
            (anchor.Anchor, anchor.Offset) = (Anchor.Left, new Vector2(1.9f, 1.2f - i * 0.7f));
            scene.Add(entity);
        }
    }

    // ----- Pieces -----

    private static void AddCamera(Scene scene)
    {
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        (view.Size, view.Background, view.Transition) = (4, Ink, SceneTransition.Fade);
        scene.Add(camera);
    }

    private static void AddBackdrop(Scene scene, string image)
    {
        var backdrop = new Entity("Backdrop");
        backdrop.Transform.Position = new Vector3(0, 0, -10);
        var sprite = backdrop.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (image, new Vector2(FightingArt.DojoWidth / 16f, FightingArt.DojoHeight / 16f), -20);
        scene.Add(backdrop);
    }

    private static Entity Tracking(string name, Vector2 at, string format, float size, Color color, string track, string output)
    {
        var label = Label(name, at, format, size, color);
        var text = label.GetComponent<Text>()!;
        (text.Track, text.Output, text.Order) = (track, output, 160);
        return label;
    }

    private static Entity Pinned(Entity label, Anchor anchor, Vector2 offset, TextAlign align)
    {
        label.GetComponent<Text>()!.Align = align;
        var pin = label.AddComponent<ScreenAnchor>();
        (pin.Anchor, pin.Offset) = (anchor, offset);
        return label;
    }

    private static Entity Hint(string value)
    {
        var hint = Label("Hint", Vector2.Zero, value, 0.3f, Dim);
        hint.GetComponent<Text>()!.Order = 160;
        var anchor = hint.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.Bottom, new Vector2(0, 0.35f));
        return hint;
    }

    private static Entity Fit(Entity button, float width)
    {
        var size = new Vector2(width, 0.85f);
        button.GetComponent<Sprite>()!.Size = size;
        button.GetComponent<Button>()!.Size = size;
        button.GetComponent<Text>()!.Size = 0.45f;
        return button;
    }
}
