using System.Numerics;
using Griff.Input;
using Griff.Modules.Fighting;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Projects;

/// <summary>
/// The Fighter template: a title screen, character selects for two players (at one keyboard, on two controllers, or one of
/// each), one against the computer and one to train, and a fight of rounds against the clock on a dojo, won by K.O. or on
/// time, the same every time and across a rewind; training's dummy and its options; exports for the desktop and the web.
/// </summary>
public sealed class FighterTemplateTests : IDisposable
{
    private const float Frame = 1 / 60f;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-fighter-" + Guid.NewGuid().ToString("N"));
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<Project> CreateProject() => await Project.CreateAsync(Path.Combine(_root, "project"), "Brawl", ProjectTemplates.Get("Fighter"));

    private Game Start(Project project, string scene, InputState input, SaveData? saves = null)
    {
        Scene Load(string name) => GameLoader.LoadScene(project, _registry, name);
        var game = new Game(Load(scene), input)
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            SceneLoader = Load,
            SceneName = scene,
            Components = _registry,
            ViewAspect = 16 / 9f,
        };
        game.UseProjectSettings(project);
        if (saves is not null)
            game.SaveData = saves;
        return game;
    }

    private static void Run(Game game, InputState input, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
            Step(game, input);
    }

    private static void Step(Game game, InputState input)
    {
        game.Tick(Frame);
        input.EndFrame();
    }

    private static void Tap(Game game, InputState input, Key key)
    {
        input.SetKey(key, true);
        Step(game, input);
        input.SetKey(key, false);
        Step(game, input);
    }

    private static void TapPad(Game game, InputState input, int pad, GamepadButton button)
    {
        input.HoldGamepadButton(pad, button, true);
        Step(game, input);
        input.HoldGamepadButton(pad, button, false);
        Step(game, input);
    }

    private static VersusMatch MatchOf(Game game) => game.Find(FighterTemplate.MatchName)!.GetComponent<VersusMatch>()!;

    private static Entity First(Game game) => game.Find(FighterTemplate.FirstFighter)!;

    private static Entity Second(Game game) => game.Find(FighterTemplate.SecondFighter)!;

    private static float HealthOf(Entity fighter) => fighter.GetComponent<Health>()!.Current;

    // Where each fighter is, what it's doing and its health, to the bit.
    private static string Fighters(Game game) => string.Join("; ", game.Scene.Entities.Where(e => e.IsActive && e.GetComponent<FightingCharacter>() is not null).Select(e =>
        $"{e.Name} {e.Prefab} {BitConverter.SingleToInt32Bits(e.Transform.Position.X)},{BitConverter.SingleToInt32Bits(e.Transform.Position.Y)} " +
        $"{e.GetComponent<Health>()!.Current:R} {e.GetComponent<FightingCharacter>()!.Move} {e.GetComponent<FightingCharacter>()!.MoveFrame} " +
        $"{e.GetComponent<Hitboxes>()!.Hitstun} {e.GetComponent<ComputerFighter>()?.Seed}"));

    // From the title to the fight: Versus, then each player picks with their own keys (Kenta on F, Mira on K).
    private static void ToTheFight(Game game, InputState input)
    {
        Run(game, input, 0.2f);
        Tap(game, input, Key.Enter);
        Run(game, input, 0.6f);
        Assert.Equal(FighterTemplate.VersusSelect, game.SceneName);
        Tap(game, input, Key.F);
        Tap(game, input, Key.K);
        Run(game, input, 1.6f);
        Assert.Equal(FighterTemplate.StageScene, game.SceneName);
    }

    // The first player walks in and jabs, again and again, until the round's over (or a minute's gone).
    private static int JabToTheEnd(Game game, InputState input, Key forward, Key punch)
    {
        var match = MatchOf(game);
        var round = match.Round;
        var steps = 0;
        input.SetKey(forward, true);
        while (match.Round == round && match.Phase == VersusPhase.Fighting && steps < 60 * 60)
        {
            input.SetKey(punch, steps % 14 == 0);
            Step(game, input);
            steps++;
        }

        input.SetKey(forward, false);
        input.SetKey(punch, false);
        return steps;
    }

    // A fighter walks towards the other until they're close.
    private static void WalkIn(Game game, InputState input, Key forward)
    {
        input.SetKey(forward, true);
        for (var i = 0; i < 60 * 4 && MathF.Abs(First(game).Transform.Position.X - Second(game).Transform.Position.X) > 1.1f; i++)
            Step(game, input);
        input.SetKey(forward, false);
    }

    private static void WaitForFight(Game game, InputState input)
    {
        for (var i = 0; i < 60 * 5 && MatchOf(game).Phase != VersusPhase.Fighting; i++)
            Step(game, input);
        Assert.Equal(VersusPhase.Fighting, MatchOf(game).Phase);
    }

    [Fact]
    public async Task Two_players_at_one_keyboard_pick_their_fighters_and_fight_two_rounds_to_a_KO_the_same_twice()
    {
        var project = await CreateProject();
        string? first = null;
        for (var run = 0; run < 2; run++)
        {
            var input = new InputState();
            var game = Start(project, Project.DefaultSceneName, input);
            ToTheFight(game, input);

            // Kenta on the left keys, Mira on the right; Round 1 is announced, and they can't move until "Fight!".
            var match = MatchOf(game);
            Assert.Equal(("Kenta", "Mira"), (match.FirstName, match.SecondName));
            Assert.Equal(("Prefabs/Kenta.prefab", "Prefabs/Mira.prefab"), (First(game).Prefab, Second(game).Prefab));
            Assert.Equal((1, 2), (First(game).GetComponent<PlayedBy>()!.Player, Second(game).GetComponent<PlayedBy>()!.Player));
            Assert.Equal(2, game.LocalPlayers.Playing);
            Assert.Equal(KeySet.Left, ((LocalPlayerInput)game.InputOf(First(game))).Keys);
            Assert.Equal(KeySet.Right, ((LocalPlayerInput)game.InputOf(Second(game))).Keys);
            Assert.Equal(VersusPhase.Intro, match.Phase);
            Assert.Equal("Round 1", match.Announcement);
            Assert.True(First(game).GetComponent<FightingCharacter>()!.Held);
            WaitForFight(game, input);
            Assert.Equal("Fight!", match.Announcement);
            Assert.Equal(60, match.TimeLeft);

            // Round 1: Kenta walks in jabbing until Mira goes down.
            JabToTheEnd(game, input, Key.D, Key.F);
            Assert.Equal(VersusPhase.RoundOver, match.Phase);
            Assert.Equal("K.O.", match.Announcement);
            Assert.Equal((1, 0), (match.FirstWins, match.SecondWins));
            Assert.True(Second(game).GetComponent<Health>()!.IsDead);
            Assert.Equal("KO", Second(game).GetComponent<Animation>()!.Clip);

            // Round 2: back in their places at full health; Mira (the right keys) goes the other way and loses again.
            WaitForFight(game, input);
            Assert.Equal(2, match.Round);
            Assert.Equal(new Vector2(-2.4f, FighterTemplate.FloorTop + 1), new Vector2(First(game).Transform.Position.X, First(game).Transform.Position.Y), new Vector2Comparer(0.05f));
            Assert.Equal(Second(game).GetComponent<Health>()!.Max, HealthOf(Second(game)));
            JabToTheEnd(game, input, Key.D, Key.F);
            Assert.Equal((2, 0), (match.FirstWins, match.SecondWins));
            Run(game, input, 3.5f);
            Assert.Equal(VersusPhase.Over, match.Phase);
            Assert.Equal("Kenta", match.Winner);
            Assert.Equal("Kenta Wins", match.Announcement);
            Assert.Equal("Winner", game.TopPanel?.Entity!.Name);

            var fighters = Fighters(game);
            if (first is null)
                first = fighters;
            else
                Assert.Equal(first, fighters);

            // Rematch starts it over with the same fighters.
            Tap(game, input, Key.Enter);
            Run(game, input, 0.8f);
            Assert.Equal(FighterTemplate.StageScene, game.SceneName);
            Assert.Equal(("Kenta", "Mira"), (MatchOf(game).FirstName, MatchOf(game).SecondName));
            Assert.Equal((0, 0), (MatchOf(game).FirstWins, MatchOf(game).SecondWins));
        }
    }

    // The fight itself, Kenta against Mira as the scene places them (or as `picks` says), with no select first.
    private Game Fight(Project project, InputState input, string first = "Kenta", string second = "Mira", bool computer = false)
    {
        var saves = new SaveData();
        saves.Set("Pick.1", first);
        saves.Set("Pick.2", second);
        saves.Set("Pick.Computer", computer);
        return Start(project, FighterTemplate.StageScene, input, saves);
    }

    [Fact]
    public async Task A_round_rewound_mid_fight_plays_on_the_same_to_the_bit()
    {
        var project = await CreateProject();
        var input = new InputState();
        var game = Fight(project, input);
        WaitForFight(game, input);

        // Kenta walks in and starts jabbing; saved in the middle of it, Mira already hurt.
        WalkIn(game, input, Key.D);
        input.SetKey(Key.D, true);
        for (var i = 0; i < 100; i++)
        {
            input.SetKey(Key.F, i % 14 == 0);
            Step(game, input);
        }

        Assert.True(HealthOf(Second(game)) < Second(game).GetComponent<Health>()!.Max);
        var timeline = new Timeline(new SceneSerializer(_registry), capacity: 4, framesPerSnapshot: 1);
        timeline.Record(game.Scene, game.Time.Total);
        var replayInput = new InputState();
        replayInput.SetKey(Key.D, true);
        var replay = new Game(timeline.Restore(0), replayInput)
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            Components = _registry,
            ViewAspect = 16 / 9f,
        };
        replay.UseProjectSettings(project);

        // The same keys on both, through to the K.O. and into the next round.
        for (var i = 100; i < 100 + 60 * 9; i++)
        {
            foreach (var keys in (InputState[])[input, replayInput])
            {
                keys.SetKey(Key.D, MatchOf(game).Phase == VersusPhase.Fighting);
                keys.SetKey(Key.F, i % 14 == 0);
            }

            Step(game, input);
            Step(replay, replayInput);
        }

        Assert.Equal(1, MatchOf(game).FirstWins);
        Assert.Equal(2, MatchOf(game).Round);
        Assert.Equal(Fighters(game), Fighters(replay));
        Assert.Equal((MatchOf(game).Phase, MatchOf(game).Round, MatchOf(game).FirstWins, MatchOf(game).Clock),
            (MatchOf(replay).Phase, MatchOf(replay).Round, MatchOf(replay).FirstWins, MatchOf(replay).Clock));
    }

    [Fact]
    public async Task Rounds_run_against_the_clock_and_more_health_wins_on_time_a_draw_wins_nothing_and_the_last_is_the_final_round()
    {
        var project = await CreateProject();
        var input = new InputState();
        var game = Fight(project, input);
        var match = MatchOf(game);
        WaitForFight(game, input);

        // One jab from Kenta, then nothing until the time's up: Kenta wins the round on time.
        void LandAJab(Key forward, Key punch, Entity target)
        {
            var before = HealthOf(target);
            WalkIn(game, input, forward);
            input.SetKey(forward, true);
            for (var i = 0; i < 60 * 4 && HealthOf(target) >= before; i++)
            {
                input.SetKey(punch, i % 14 == 0);
                Step(game, input);
            }

            input.SetKey(forward, false);
            input.SetKey(punch, false);
            Assert.True(HealthOf(target) < before);
        }

        LandAJab(Key.D, Key.F, Second(game));
        Run(game, input, 30);
        Assert.InRange(match.TimeLeft, 25, 31);
        Run(game, input, 31);
        Assert.Equal(VersusPhase.RoundOver, match.Phase);
        Assert.Equal("Time", match.Announcement);
        Assert.Equal((1, 0), (match.FirstWins, match.SecondWins));

        // Round 2: Mira (the right keys, facing left) lands one and wins it on time.
        WaitForFight(game, input);
        Assert.Equal("Round 2", "Round " + match.Round);
        LandAJab(Key.Left, Key.K, First(game));
        Run(game, input, 61);
        Assert.Equal((1, 1), (match.FirstWins, match.SecondWins));

        // One each: the final round. Nobody lands anything: a draw, and the final round again.
        for (var i = 0; i < 60 * 5 && match.Phase != VersusPhase.Intro; i++)
            Step(game, input);
        Assert.Equal("Final Round", match.Announcement);
        WaitForFight(game, input);
        Run(game, input, 61);
        Assert.Equal("Draw", match.Announcement);
        Assert.Equal((1, 1), (match.FirstWins, match.SecondWins));
        for (var i = 0; i < 60 * 5 && match.Phase != VersusPhase.Intro; i++)
            Step(game, input);
        Assert.Equal(("Final Round", 4), (match.Announcement, match.Round));

        // Kenta lands one more: his match.
        WaitForFight(game, input);
        LandAJab(Key.D, Key.F, Second(game));
        Run(game, input, 61 + 3);
        Assert.Equal(VersusPhase.Over, match.Phase);
        Assert.Equal(("Kenta", "Kenta Wins"), (match.Winner, match.Announcement));
    }

    [Fact]
    public async Task One_player_on_the_keyboard_and_one_on_a_controller_pick_and_fight_each_on_their_own()
    {
        var project = await CreateProject();
        var input = new InputState();
        input.SetGamepadConnected(0, true);
        var game = Start(project, FighterTemplate.VersusSelect, input);
        Run(game, input, 0.2f);
        var select = game.Find(FighterTemplate.SelectName)!.GetComponent<CharacterSelect>()!;
        Assert.Equal(("Keyboard", "Gamepad 1"), (select.FirstControls, select.SecondControls));

        // The controller's player moves to Kenta and picks him with X; the keyboard's moves to Mira (right arrow) and picks her
        // with J (Punch on the whole keyboard).
        TapPad(game, input, 0, GamepadButton.DPadLeft);
        Assert.Equal(("Kenta", "Kenta"), (select.FirstName, select.SecondName));
        Tap(game, input, Key.Right);
        Assert.Equal(("Mira", "Kenta"), (select.FirstName, select.SecondName));
        TapPad(game, input, 0, GamepadButton.X);
        Assert.True(select.SecondPicked);
        Assert.False(select.FirstPicked);
        Tap(game, input, Key.J);
        Run(game, input, 1.6f);
        Assert.Equal(FighterTemplate.StageScene, game.SceneName);
        Assert.Equal(("Mira", "Kenta"), (MatchOf(game).FirstName, MatchOf(game).SecondName));
        WaitForFight(game, input);

        // The arrows move the first player, the controller's D-pad the second.
        var (first, second) = (First(game).Transform.Position.X, Second(game).Transform.Position.X);
        input.SetKey(Key.Right, true);
        Run(game, input, 0.5f);
        input.SetKey(Key.Right, false);
        Assert.True(First(game).Transform.Position.X > first + 0.5f);
        Assert.Equal(second, Second(game).Transform.Position.X, 3);

        input.HoldGamepadButton(0, GamepadButton.DPadLeft, true);
        Run(game, input, 0.5f);
        input.HoldGamepadButton(0, GamepadButton.DPadLeft, false);
        Assert.True(Second(game).Transform.Position.X < second - 0.5f);

        // The controller's X is the second player's punch, not the first's.
        var moves = new List<string>();
        First(game).GetComponent<FightingCharacter>()!.MoveStarted += m => moves.Add("first " + m.Name);
        Second(game).GetComponent<FightingCharacter>()!.MoveStarted += m => moves.Add("second " + m.Name);
        TapPad(game, input, 0, GamepadButton.X);
        Run(game, input, 0.5f);
        Tap(game, input, Key.J);
        Run(game, input, 0.5f);
        Assert.Equal(["second Jab", "first Jab"], moves);
    }

    [Fact]
    public async Task Two_controllers_each_play_their_own_fighter_and_swapping_controls_is_kept()
    {
        var project = await CreateProject();
        var input = new InputState();
        input.SetGamepadConnected(0, true);
        input.SetGamepadConnected(1, true);
        var saves = new SaveData();
        var game = Start(project, FighterTemplate.VersusSelect, input, saves);
        Run(game, input, 0.2f);
        var select = game.Find(FighterTemplate.SelectName)!.GetComponent<CharacterSelect>()!;
        Assert.Equal(("Keyboard and Gamepad 1", "Gamepad 2"), (select.FirstControls, select.SecondControls));

        // Each picks with their own controller's X.
        TapPad(game, input, 1, GamepadButton.X);
        Assert.Equal((false, true), (select.FirstPicked, select.SecondPicked));
        TapPad(game, input, 0, GamepadButton.X);
        Run(game, input, 1.6f);
        Assert.Equal(("Kenta", "Mira"), (MatchOf(game).FirstName, MatchOf(game).SecondName));
        WaitForFight(game, input);

        // Each controller's stick moves its own fighter.
        var (first, second) = (First(game).Transform.Position.X, Second(game).Transform.Position.X);
        input.SetGamepad(1, [], new Vector2(-1, 0), Vector2.Zero);
        Run(game, input, 0.5f);
        input.SetGamepad(1, [], Vector2.Zero, Vector2.Zero);
        Assert.Equal(first, First(game).Transform.Position.X, 3);
        Assert.True(Second(game).Transform.Position.X < second - 0.5f);
        input.SetGamepad(0, [], new Vector2(1, 0), Vector2.Zero);
        Run(game, input, 0.5f);
        input.SetGamepad(0, [], Vector2.Zero, Vector2.Zero);
        Assert.True(First(game).Transform.Position.X > first + 0.5f);

        // One controller unplugged: one at the keyboard, one on the controller left; Swap Controls (Tab) on the select screen
        // changes them over, and it's kept with the player's settings.
        input.SetGamepadConnected(1, false);
        game.LoadScene(FighterTemplate.VersusSelect);
        Run(game, input, 0.5f);
        select = game.Find(FighterTemplate.SelectName)!.GetComponent<CharacterSelect>()!;
        Assert.Equal(("Keyboard", "Gamepad 1"), (select.FirstControls, select.SecondControls));
        Tap(game, input, Key.Tab);
        Assert.Equal(("Gamepad 1", "Keyboard"), (select.FirstControls, select.SecondControls));
        Assert.Equal(PlayerDevice.Gamepad1, game.LocalPlayers.Chosen(1));
        Assert.Contains("Gamepad1", saves.GetString(LocalPlayers.SaveKey));
    }

    [Fact]
    public async Task One_player_against_the_computer_which_fights_back_the_same_every_time()
    {
        var project = await CreateProject();
        string? first = null;
        for (var run = 0; run < 2; run++)
        {
            var input = new InputState();
            var game = Start(project, FighterTemplate.SoloSelect, input);
            Run(game, input, 0.2f);
            var select = game.Find(FighterTemplate.SelectName)!.GetComponent<CharacterSelect>()!;
            Assert.Equal(("Keyboard and mouse", "Computer"), (select.FirstControls, select.SecondControls));

            // J (Punch on the whole keyboard) picks Kenta; the computer takes Mira.
            Tap(game, input, Key.J);
            Assert.Equal((true, true, "Mira"), (select.FirstPicked, select.SecondPicked, select.SecondName));
            Run(game, input, 1.6f);
            Assert.Equal(FighterTemplate.StageScene, game.SceneName);
            var computer = Second(game).GetComponent<ComputerFighter>();
            Assert.NotNull(computer);
            Assert.Equal(ComputerBehavior.Fight, computer.Behavior);
            Assert.Null(Second(game).GetComponent<PlayedBy>());
            Assert.Equal(1, game.LocalPlayers.Playing);

            // Left alone, Kenta is walked up to and beaten on.
            WaitForFight(game, input);
            var moves = new List<string>();
            Second(game).GetComponent<FightingCharacter>()!.MoveStarted += m => moves.Add(m.Name);
            Run(game, input, 8);
            Assert.True(HealthOf(First(game)) < First(game).GetComponent<Health>()!.Max, "The computer never hit.");
            Run(game, input, 25);
            Assert.True(MatchOf(game).SecondWins >= 1, "The computer never won a round.");
            Assert.True(moves.Distinct().Count() >= 2, string.Join(", ", moves));

            // The keys still move Kenta (and only him).
            var x = First(game).Transform.Position.X;
            input.SetKey(Key.A, true);
            Run(game, input, 0.3f);
            input.SetKey(Key.A, false);
            if (MatchOf(game).Phase == VersusPhase.Fighting && !First(game).GetComponent<FightingCharacter>()!.Held)
                Assert.NotEqual(x, First(game).Transform.Position.X);

            var fighters = Fighters(game) + " " + string.Join(",", moves);
            if (first is null)
                first = fighters;
            else
                Assert.Equal(first, fighters);
        }
    }

    [Fact]
    public async Task Training_has_no_clock_a_dummy_whose_guard_and_stance_change_hitboxes_shown_and_a_dummy_that_gets_up()
    {
        var project = await CreateProject();
        var input = new InputState();
        var game = Start(project, FighterTemplate.TrainingSelect, input);
        Run(game, input, 0.2f);
        Tap(game, input, Key.Right);
        Tap(game, input, Key.J);
        Run(game, input, 1.6f);
        Assert.Equal(FighterTemplate.TrainingScene, game.SceneName);
        Run(game, input, 0.2f);

        // Mira against a Kenta dummy that stands there; no rounds, no clock.
        var match = MatchOf(game);
        Assert.Equal(("Mira", "Kenta"), (match.FirstName, match.SecondName));
        Assert.Equal(VersusPhase.Fighting, match.Phase);
        Assert.Equal(0, match.TimeLeft);
        var dummy = Second(game);
        var cpu = dummy.GetComponent<ComputerFighter>()!;
        Assert.Equal(ComputerBehavior.Stand, cpu.Behavior);
        Assert.Equal("1  Block: Never", game.Find("Dummy Block")!.GetComponent<Text>()!.Shown);

        // 1: it blocks standing; Mira's jabs are blocked.
        Tap(game, input, Key.D1);
        Assert.Equal(AutoBlock.Standing, dummy.GetComponent<FightingCharacter>()!.AutoBlock);
        Run(game, input, 0.1f);
        Assert.Equal("1  Block: Standing", game.Find("Dummy Block")!.GetComponent<Text>()!.Shown);
        var blocks = 0;
        dummy.GetComponent<FightingCharacter>()!.Blocked += _ => blocks++;
        WalkIn(game, input, Key.D);
        input.SetKey(Key.D, true);
        for (var i = 0; i < 120; i++)
        {
            input.SetKey(Key.J, i % 14 == 0);
            Step(game, input);
        }

        input.SetKey(Key.D, false);
        input.SetKey(Key.J, false);
        Assert.True(blocks > 0);

        // 2: it crouches; 3: the hitboxes show; 4: back to where they started.
        Tap(game, input, Key.D2);
        Assert.Equal(ComputerBehavior.Crouch, cpu.Behavior);
        Run(game, input, 0.2f);
        Assert.True(dummy.GetComponent<FightingCharacter>()!.Crouching);
        Tap(game, input, Key.D3);
        Assert.True(game.ShowHitboxes);
        Run(game, input, 0.05f);
        Assert.Equal("3  Hitboxes: On", game.Find("Show Hitboxes")!.GetComponent<Text>()!.Shown);
        Tap(game, input, Key.D4);
        Assert.Equal(match.FirstStart.X, First(game).Transform.Position.X, 2);

        // Knocked out, it gets up again a moment later, at full health.
        dummy.GetComponent<Health>()!.Kill();
        Run(game, input, 0.5f);
        Assert.True(dummy.GetComponent<Health>()!.IsDead);
        Run(game, input, 1);
        Assert.Equal(dummy.GetComponent<Health>()!.Max, HealthOf(dummy));
        Assert.Equal(VersusPhase.Fighting, match.Phase);
    }

    [Fact]
    public async Task Exports_for_the_desktop_and_the_web_carry_the_fighters_and_play()
    {
        var project = await CreateProject();
        string[] wanted = ["Assets/Prefabs/Kenta.prefab", "Assets/Prefabs/Mira.prefab", "Assets/Fighting/mira.png", "Assets/Fighting/dojo.png",
            "Assets/Sounds/punch.wav", "Assets/Sounds/bell.wav", "Assets/Scenes/Stage.scene", "Assets/Scenes/Training.scene", "Assets/Scenes/Select.scene"];

        var player = Path.Combine(_root, "player", "Griff.Player.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(player)!);
        File.WriteAllText(player, "player exe");
        var desktop = GameExporter.Export(project, player, Path.Combine(_root, "out"));
        Assert.True(desktop.Success, desktop.Error);
        var extracted = GamePackage.Extract(desktop.Executable!, Path.Combine(_root, "cache"), "Brawl")!;
        Assert.All(wanted, file => Assert.True(File.Exists(Path.Combine(extracted, file)), file));
        var loaded = await GameLoader.LoadAsync(extracted);
        Assert.Empty(loaded.Problems);

        // The exported game: picked on the select screen, and fought.
        var input = new InputState();
        var game = (loaded with { Scene = loaded.LoadScene(Project.DefaultSceneName), SceneName = Project.DefaultSceneName }).NewGame(new GameHostOptions { Input = input });
        game.ViewAspect = 16 / 9f;
        ToTheFight(game, input);
        WaitForFight(game, input);
        JabToTheEnd(game, input, Key.D, Key.F);
        Assert.Equal(1, MatchOf(game).FirstWins);

        var web = Path.Combine(_root, "web-player");
        Directory.CreateDirectory(Path.Combine(web, "_framework"));
        File.WriteAllText(Path.Combine(web, WebExporter.Page), "<html>");
        var site = WebExporter.Export(project, web, Path.Combine(_root, "web-out"));
        Assert.True(site.Success, site.Error);
        var gameFolder = Path.Combine(_root, "web-out", ExportLayout.GameFolder);
        var listed = File.ReadAllLines(Path.Combine(gameFolder, ExportLayout.WebFileList));
        Assert.All(wanted, file => Assert.Contains(file, listed));
        Assert.All(wanted, file => Assert.True(File.Exists(Path.Combine(gameFolder, file)), file));
    }

    [Fact]
    public async Task Hits_blocks_announcements_and_picks_have_their_sounds()
    {
        var project = await CreateProject();
        var input = new InputState();
        var game = Fight(project, input);
        var match = MatchOf(game);
        Assert.Equal((FighterTemplate.BellSound, FighterTemplate.FightSound, FighterTemplate.BellSound), (match.RoundSound, match.FightSound, match.KoSound));
        Assert.Equal(FighterTemplate.PunchSound, First(game).GetComponent<Hitboxes>()!.HitSound);
        Assert.Equal(FighterTemplate.BlockSound, First(game).GetComponent<FightingCharacter>()!.BlockSound);
        Assert.Equal((FighterTemplate.GruntSound, FighterTemplate.KoSound), (Second(game).GetComponent<Health>()!.HurtSound, Second(game).GetComponent<Health>()!.DeathSound));
        Assert.Contains(First(game).GetComponent<FightingCharacter>()!.Moves, m => m.Contains(FighterTemplate.WhooshSound, StringComparison.Ordinal));
        var select = Start(project, FighterTemplate.VersusSelect, input).Find(FighterTemplate.SelectName)!.GetComponent<CharacterSelect>()!;
        Assert.Equal((TemplateSounds.Select, FighterTemplate.PickSound), (select.MoveSound, select.PickSound));
    }

    private sealed class Vector2Comparer(float tolerance) : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector2 obj) => 0;
    }
}
