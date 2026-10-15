using System.Numerics;
using Griff.Audio;
using Griff.Input;
using Griff.Modules.PhysicsGames;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// A level's goal: won once its targets are down and things have settled, with a bonus for each shot left and one to three
/// stars by the score, the best kept in the save data; lost once the shots run out with targets standing; its actions,
/// outputs, rewinds and garbage. And the stars that show how it went.
/// </summary>
public sealed class LevelGoalTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;
    private readonly RecordingAudio _audio = new();
    private readonly InputState _input = new();
    private readonly List<(string Message, object? Data)> _heard = [];

    public LevelGoalTests()
    {
        _game = new Game(_scene, _input) { Audio = _audio, SaveData = new SaveData() };
        _game.On("Level Complete", data => _heard.Add(("Level Complete", data)));
        _game.On("Level Failed", data => _heard.Add(("Level Failed", data)));
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
            _game.Tick(Frame);
    }

    // A level: a floor, a slingshot with `shots` balls, `targets` targets standing on the floor, a score, and its goal.
    private LevelGoal Level(int shots = 3, int targets = 2, string? saveAs = "Level1")
    {
        var floor = new Entity("Floor");
        floor.Transform.Position = new Vector3(0, -0.5f, 0);
        floor.AddComponent<BoxCollider>().Size = new Vector2(200, 1);
        _scene.Add(floor);

        var slingshot = new Entity("Slingshot");
        slingshot.Transform.Position = new Vector3(-8, 0, 0);
        slingshot.AddComponent<Slingshot>().LoadDelay = 0.5f;
        _scene.Add(slingshot);
        for (var i = 0; i < shots; i++)
        {
            var shot = new Entity("Ball");
            shot.AddComponent<CircleCollider>().Radius = 0.3f;
            shot.AddComponent<Body>().Rotates = true;
            _scene.Add(shot, slingshot);
        }

        for (var i = 0; i < targets; i++)
        {
            var target = new Entity("Target");
            target.Transform.Position = new Vector3(4 + i * 2, 0.4f, 0);
            target.AddComponent<CircleCollider>().Radius = 0.4f;
            target.AddComponent<Body>().Asleep = true;
            var breakable = target.AddComponent<Breakable>();
            (breakable.Strength, breakable.Points, breakable.BreakMessage) = (3, 5000, "Score");
            _scene.Add(target);
        }

        var score = new Entity("Score");
        score.AddComponent<Text>();
        score.AddComponent<Counter>();
        _scene.Add(score);

        var level = new Entity("Level");
        var goal = level.AddComponent<LevelGoal>();
        (goal.SaveAs, goal.TwoStars, goal.ThreeStars, goal.BonusSound, goal.WinSound, goal.LoseSound) =
            (saveAs, 20000, 30000, "Sounds/coin.wav", "Sounds/win.wav", "Sounds/lose.wav");
        _scene.Add(level);
        return goal;
    }

    private void Clear()
    {
        foreach (var root in _scene.Roots.ToList())
            _scene.Remove(root);
        _game.Tick(Frame);
    }

    private Slingshot Slingshot => _scene.Entities.Single(e => e.Name == "Slingshot").GetComponent<Slingshot>()!;

    private int Score => _scene.Entities.Single(e => e.Name == "Score").GetComponent<Counter>()!.Value;

    private void Break(string name)
    {
        foreach (var target in _scene.Entities.Where(e => e.Name == name).ToList())
            target.GetComponent<Breakable>()!.Break();
    }

    // Launches the loaded shot backwards off the left, where it lands and comes to rest well away from everything.
    private void Waste()
    {
        for (var i = 0; i < 600 && !Slingshot.Loaded; i++)
            _game.Tick(Frame);
        Slingshot.Pull = new Vector2(0.6f, 0.1f);
        _game.Do(Slingshot.Entity!, "Launch");
        Run(Frame);
    }

    [Fact]
    public void It_counts_its_targets_and_once_theyre_down_and_settled_counts_a_bonus_a_shot_then_gives_the_stars()
    {
        var goal = Level();
        Run(0.2f);
        Assert.Equal((2, 2, LevelPhase.Playing), (goal.TargetCount, goal.TargetsLeft, goal.Phase));

        Break("Target");
        Run(Frame * 2);
        Assert.Equal(0, goal.TargetsLeft);
        Assert.Equal(LevelPhase.Settling, goal.Phase);
        Assert.False(Slingshot.Controllable);
        Assert.Equal(10000, Score);

        // Two seconds to settle, then the three shots' bonus, one after another, each with its sound and points shown.
        Run(1.9f);
        Assert.Equal(LevelPhase.Settling, goal.Phase);
        Run(0.2f);
        Assert.Equal(LevelPhase.Bonus, goal.Phase);
        Run(LevelGoalSystem.BonusPace + Frame);
        Assert.Equal(20000, Score);
        Assert.Equal(2, Slingshot.ShotsLeft);
        Assert.Contains(_scene.Entities, e => e.Name == "Points" && e.GetComponent<Text>()!.Value == "10,000");
        Run(LevelGoalSystem.BonusPace * 3);
        Assert.Equal(40000, Score);
        Assert.Equal(0, Slingshot.ShotsLeft);
        Assert.Equal(3, _audio.Calls.Count(c => c == "play Sounds/coin.wav"));

        Assert.Equal(LevelPhase.Won, goal.Phase);
        Assert.True(goal.Won);
        Assert.Equal(3, goal.Stars);
        Assert.Equal([("Level Complete", (object?)3)], _heard);
        Assert.Contains("play Sounds/win.wav", _audio.Calls);
        Assert.Equal(3, _game.SaveData.GetInt("Level1.Stars"));
        Assert.Equal(40000, _game.SaveData.GetInt("Level1.Best"));
        Assert.Equal((3, 40000), (goal.BestStars, goal.BestScore));
    }

    [Theory]
    [InlineData(19999, 1)]
    [InlineData(20000, 2)]
    [InlineData(29999, 2)]
    [InlineData(30000, 3)]
    public void Its_stars_are_one_for_winning_and_more_for_a_higher_score(int score, int stars) =>
        Assert.Equal(stars, new LevelGoal { TwoStars = 20000, ThreeStars = 30000 }.StarsFor(score));

    [Fact]
    public void Out_of_shots_with_targets_standing_it_waits_for_things_to_settle_then_says_its_lost()
    {
        var goal = Level(shots: 2);
        Run(0.2f);
        Waste();
        Run(2.5f);
        Waste();
        for (var i = 0; i < 60 * 6 && !Slingshot.OutOfShots; i++)
            _game.Tick(Frame);
        Assert.True(Slingshot.OutOfShots);
        Assert.Equal(LevelPhase.Settling, goal.Phase);
        Assert.True(Slingshot.Controllable);

        // A target knocked out while it settles still counts; the rest are still standing.
        Break("Target");
        Run(0.1f);
        Assert.Equal(0, goal.TargetsLeft);
        Run(2.1f);
        Assert.Equal(LevelPhase.Bonus, goal.Phase);
        Run(1);
        Assert.Equal(LevelPhase.Won, goal.Phase);
        Assert.Equal(10000, Score);
        Assert.Equal(1, goal.Stars);

        Clear();
        _heard.Clear();
        goal = Level(shots: 1);
        Run(0.2f);
        Waste();
        for (var i = 0; i < 60 * 8 && goal.Phase != LevelPhase.Lost; i++)
            _game.Tick(Frame);
        Assert.True(goal.Lost);
        Assert.Equal([("Level Failed", (object?)2)], _heard);
        Assert.Contains("play Sounds/lose.wav", _audio.Calls);
    }

    [Fact]
    public void A_worse_win_keeps_the_best_and_its_actions_win_or_lose_it_now()
    {
        _game.SaveData.Set("Level1.Stars", 3);
        _game.SaveData.Set("Level1.Best", 90000);
        var goal = Level(shots: 1);
        Run(0.2f);
        Assert.Equal((3, 90000), (goal.BestStars, goal.BestScore));

        _game.Do(goal.Entity!, "Win");
        Run(1);
        Assert.Equal(LevelPhase.Won, goal.Phase);
        Assert.Equal(1, goal.Stars);
        Assert.Equal(3, _game.SaveData.GetInt("Level1.Stars"));
        Assert.Equal(90000, _game.SaveData.GetInt("Level1.Best"));

        Clear();
        goal = Level();
        Run(0.2f);
        _game.Do(goal.Entity!, "Lose");
        Run(Frame);
        Assert.True(goal.Lost);
        _game.Do(goal.Entity!, "Win");
        Run(1);
        Assert.True(goal.Lost);
    }

    [Fact]
    public void A_level_with_no_targets_isnt_won_and_without_save_as_nothing_is_saved()
    {
        var goal = Level(targets: 0, saveAs: null);
        Run(1);
        Assert.Equal(LevelPhase.Playing, goal.Phase);

        _game.Do(goal.Entity!, "Win");
        Run(3);
        Assert.True(goal.Won);
        Assert.Empty(_game.SaveData.Keys);
        Assert.Equal(goal.Stars, goal.BestStars);
    }

    [Fact]
    public void Its_outputs_are_for_texts_and_its_state_comes_back_with_a_rewind()
    {
        var goal = Level();
        var text = new Entity("Left");
        var shown = text.AddComponent<Text>();
        (shown.Value, shown.Track, shown.Output) = ("{0} of {1}", "Level", "Targets Left");
        _scene.Add(text);
        Run(0.2f);
        Assert.Equal("2 of 2", shown.Shown);

        Break("Target");
        Run(2.3f);
        Assert.Equal(LevelPhase.Bonus, goal.Phase);
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var saved = serializer.SaveToString(_scene);
        var restored = serializer.LoadFromString(saved);
        var again = restored.Entities.Single(e => e.Name == "Level").GetComponent<LevelGoal>()!;
        Assert.Equal((LevelPhase.Bonus, 2, goal.Timer), (again.Phase, again.TargetCount, again.Timer));

        var replay = new Game(restored, new InputState()) { SaveData = new SaveData() };
        for (var i = 0; i < 120; i++)
        {
            replay.Tick(Frame);
            _game.Tick(Frame);
        }

        Assert.Equal(LevelPhase.Won, again.Phase);
        Assert.Equal(goal.Stars, again.Stars);
        Assert.Equal(Score, restored.Entities.Single(e => e.Name == "Score").GetComponent<Counter>()!.Value);
    }

    [Fact]
    public void Playing_it_makes_no_garbage()
    {
        Level();
        Run(0.5f);
        var playing = Allocations.PerCall(() => _game.Tick(Frame), 30);
        Assert.True(playing == 0, $"A step allocated {playing:N0} bytes.");
    }

    [Fact]
    public void A_hud_of_prompts_a_button_and_a_camera_kept_inside_the_level_makes_no_garbage_and_prompts_follow_the_device()
    {
        Level();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>();
        var follow = camera.AddComponent<Follow>();
        (follow.Target, follow.StayInside) = ("Slingshot", "Bounds");
        _scene.Add(camera);
        var bounds = new Entity("Bounds");
        var area = bounds.AddComponent<BoxCollider>();
        (area.Size, area.IsTrigger) = (new Vector2(40, 20), true);
        _scene.Add(bounds);
        var hint = new Entity("Hint");
        var text = hint.AddComponent<Text>();
        text.Value = "Pull back and {Jump}";
        _scene.Add(hint);
        var retry = new Entity("Retry");
        retry.AddComponent<Button>().Navigable = false;
        _scene.Add(retry);
        for (var i = 0; i < 30; i++)
            _game.Tick(Frame);
        Assert.Equal("Pull back and Space", text.Shown);

        var steps = Allocations.PerCall(() => _game.Tick(Frame), 30);
        Assert.True(steps == 0, $"A step allocated {steps:N0} bytes.");

        // A gamepad picked up, then a key rebound: the prompt follows each.
        _input.SetGamepad([GamepadButton.A], Vector2.Zero);
        _game.Tick(Frame);
        Assert.Equal("Pull back and A", text.Shown);
        _input.SetGamepad([], Vector2.Zero);
        _input.SetKey(Key.Enter, true);
        _game.Tick(Frame);
        _input.Rebind("Jump", Key.W);
        _game.Tick(Frame);
        Assert.Equal("Pull back and W", text.Shown);
    }

    // ----- Stars

    [Fact]
    public void Stars_follow_an_output_or_the_save_data_and_fill_one_after_another_with_a_sound()
    {
        var goal = Level();
        var row = new Entity("Rating");
        var stars = row.AddComponent<Stars>();
        (stars.Track, stars.Output, stars.Delay, stars.Sound) = ("Level", "Stars", 0.4f, "Sounds/star.wav");
        _scene.Add(row);
        var saved = new Entity("Best Rating");
        var best = saved.AddComponent<Stars>();
        best.Saved = "Level1.Stars";
        _scene.Add(saved);
        Run(0.2f);
        Assert.Equal((0, 0), (stars.Value, stars.Shown));

        _game.Do(goal.Entity!, "Win");
        for (var i = 0; i < 600 && !goal.Won; i++)
            _game.Tick(Frame);
        Assert.Equal(3, stars.Value);

        // Each star pops in its Delay after the last, with its sound; the best from the save data shows straight away.
        Run(Frame);
        Assert.Equal(3, best.Shown);
        Assert.Equal(0, stars.Shown);
        Run(0.4f);
        Assert.Equal(1, stars.Shown);
        Run(0.4f);
        Assert.Equal(2, stars.Shown);
        Run(0.4f);
        Assert.Equal(3, stars.Shown);
        Assert.Equal(3, _audio.Calls.Count(c => c == "play Sounds/star.wav"));

        // Drawn: five diamonds a star, and a rim and a gleam for each filled one.
        var drawn = SceneRenderer.Build(_scene, 1).Sprites.Count(d => d.Entity == row);
        Assert.Equal(3 * 15, drawn);
        best.Count = 5;
        best.Rim = Color.Transparent;
        Assert.Equal(3 * 10 + 2 * 5, SceneRenderer.Build(_scene, 1).Sprites.Count(d => d.Entity == saved));
    }

    [Fact]
    public void Stars_of_their_own_value_show_it_at_once_and_go_on_under_a_pause()
    {
        var row = new Entity("Rating");
        var stars = row.AddComponent<Stars>();
        stars.Value = 2;
        _scene.Add(row);
        _game.Tick(Frame);
        Assert.Equal(2, stars.Shown);

        stars.Delay = 0.3f;
        stars.Value = 3;
        _game.Time.Scale = 0;
        Run(0.35f);
        Assert.Equal(3, stars.Shown);
        stars.Value = 9;
        _game.Tick(Frame);
        Assert.Equal(3, stars.Shown);
    }

    private sealed class RecordingAudio : IAudio
    {
        public List<string> Calls { get; } = [];

        public float Volume { get; set; } = 1;

        public float MusicVolume { get; set; } = 1;

        public float SoundVolume { get; set; } = 1;

        public bool Muted { get; set; }

        public void Play(string sound, float volume = 1f) => Calls.Add($"play {sound}");

        public void PlayMusic(string music, float volume = 0.6f, bool loop = true) => Calls.Add($"music {music}");

        public void StopMusic() => Calls.Add("stop music");

        public void StopAll() => Calls.Add("stop all");
    }
}
