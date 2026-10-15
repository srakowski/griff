using System.Numerics;
using Griff.Data;
using Griff.Input;
using Griff.Modules.Fighting;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>
/// Fighters: moves written in numpad notation read and written; motions (quarter circles, dragon punches, charges) read
/// from the input history as the fighter faces; a press buffered a few steps early; startup, active and recovery frames
/// over the clip, its hitboxes hitting only while active; cancels once a move connects; blocking high and low with chip
/// damage and blockstun; hitstun locking a fighter out and combos counting; facing the opponent; moves from a table; the
/// same to the bit twice and across a rewind, in a network game, and with no garbage.
/// </summary>
public sealed class FighterTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene;
    private readonly Game _game;
    private readonly InputState _keys = new();
    private readonly Entity _fighter;
    private readonly Entity _dummy;

    public FighterTests()
    {
        _scene = Training(out _fighter, out _dummy);
        _game = new Game(_scene, _keys) { PrefabLoader = name => new Entity(name) };
        // No hit stop, so a move's steps are counted without the freeze a hit makes.
        _fighter.GetComponent<Hitboxes>()!.HitStop = 0;
        _dummy.GetComponent<Hitboxes>()!.HitStop = 0;
        Step(10);
    }

    // The training pair from Entity > Genres > Fighting: the fighter at -1.2 facing right, the dummy at 1.2 facing it.
    private static Scene Training(out Entity fighter, out Entity dummy, bool loose = false)
    {
        var scene = new Scene();
        var preset = new TrainingPreset();
        var root = new Entity(preset.EntityName(scene));
        preset.Configure(root, scene);
        scene.Add(root);
        foreach (var child in preset.CreateChildren(scene))
            scene.Add(child, loose ? null : root);
        fighter = scene.Entities.Single(e => e.Name == "Fighter");
        dummy = scene.Entities.Single(e => e.Name == "Dummy");
        return scene;
    }

    private static FightingCharacter FighterOf(Entity entity) => entity.GetComponent<FightingCharacter>()!;

    private static float HealthOf(Entity entity) => entity.GetComponent<Health>()!.Current;

    private void Step(int steps = 1)
    {
        for (var i = 0; i < steps; i++)
        {
            _game.Tick(Frame);
            _keys.EndFrame();
        }
    }

    // Holds what each step says, a step each, in numpad notation for a fighter facing right ("2", "3", "6P", "5", "4K"):
    // a button is pressed on the step it's named, and let go after. Each step can be repeated, "4*40".
    private void Play(params string[] steps) => Play(_game, _keys, steps);

    private static void Play(Game game, InputState keys, params string[] steps)
    {
        foreach (var token in steps)
        {
            var star = token.IndexOf('*');
            var (step, times) = star > 0 ? (token[..star], int.Parse(token[(star + 1)..], System.Globalization.CultureInfo.InvariantCulture)) : (token, 1);
            for (var t = 0; t < times; t++)
            {
                var direction = step[0] - '0';
                var (x, y) = ((direction - 1) % 3 - 1, (direction - 1) / 3 - 1);
                keys.SetKey(Key.A, x < 0);
                keys.SetKey(Key.D, x > 0);
                keys.SetKey(Key.S, y < 0);
                keys.SetKey(Key.W, y > 0);
                keys.SetKey(Key.U, t == 0 && step.Contains('P'));
                keys.SetKey(Key.I, t == 0 && step.Contains('K'));
                game.Tick(Frame);
                keys.EndFrame();
            }
        }
    }

    // Steps until the fighter starts a move, giving its name (null if none came out within the steps).
    private string? Next(Entity entity, int steps = 60)
    {
        string? started = null;
        void Heard(FighterMove move) => started ??= move.Name;
        FighterOf(entity).MoveStarted += Heard;
        for (var i = 0; i < steps && started is null; i++)
            Play("5");
        FighterOf(entity).MoveStarted -= Heard;
        return started;
    }

    private string? Started(Action play)
    {
        string? started = null;
        void Heard(FighterMove move) => started ??= move.Name;
        FighterOf(_fighter).MoveStarted += Heard;
        play();
        FighterOf(_fighter).MoveStarted -= Heard;
        return started;
    }

    [Fact]
    public void Inputs_are_read_written_described_and_drawn_as_arrows()
    {
        var qcf = MoveInput.Parse("236P");
        Assert.Equal(("236", "P", false, 0), (qcf.Motion, qcf.Button, qcf.Air, qcf.Charge));
        Assert.True(qcf.IsSpecial);
        Assert.Equal("quarter circle forward + P", qcf.Describe());
        Assert.Equal("↓↘→ P", qcf.Arrows());
        Assert.Equal("dragon punch + K", MoveInput.Parse("623k").Describe());
        Assert.Equal("[4]6P", MoveInput.Parse("[4] 6 + P").ToString());
        Assert.Equal("charge back, forward + P", MoveInput.Parse("[4]6P").Describe());
        Assert.Equal("[←]→ P", MoveInput.Parse("[4]6P").Arrows());
        Assert.Equal("j.2K", MoveInput.Parse("j2K").ToString());
        Assert.Equal("down + K in the air", MoveInput.Parse("j.2K").Describe());
        Assert.False(MoveInput.Parse("5P").IsSpecial);
        Assert.Equal("HP", MoveInput.Parse("236HP").Button);

        Assert.False(MoveInput.TryParse("236", out _, out var noButton));
        Assert.Equal("it needs a button after the directions, e.g. 236P", noButton);
        Assert.False(MoveInput.TryParse("206P", out _, out var zero));
        Assert.Contains("isn't a direction", zero);
        Assert.False(MoveInput.TryParse("[5]6P", out _, out _));
        Assert.False(MoveInput.TryParse("[4]P", out _, out _));
    }

    [Fact]
    public void Moves_are_read_and_written_as_lines()
    {
        var move = FighterMove.Parse("Dragon Punch: 623P clip Rising frames 3/6/22 high blockstun 9 chip 0.2 push 1,7 message Shoryu sound up.wav cancels Low Kick, special");
        Assert.Equal("Dragon Punch", move.Name);
        Assert.Equal("Rising", move.ClipName);
        Assert.Equal((3, 6, 22, MoveHeight.High, 9, 0.2f), (move.Startup, move.Active, move.Recovery, move.Hits, move.Blockstun, move.Chip!.Value));
        Assert.Equal(new Vector2(1, 7), move.Push);
        Assert.Equal(["Low Kick", "special"], move.CancelList);
        Assert.Equal("Dragon Punch: 623P clip Rising frames 3/6/22 high blockstun 9 chip 0.2 push 1,7 message Shoryu sound up.wav cancels Low Kick, special", move.ToString());
        Assert.Equal("LowKick", FighterMove.Parse("Low Kick: 2K low").ClipName);
        Assert.Equal("Jab: 5P", FighterMove.Parse("Jab: 5P cancels none").ToString());

        Assert.True(move.CancelsInto(FighterMove.Parse("Fireball: 236P")));
        Assert.True(move.CancelsInto(FighterMove.Parse("Low Kick: 2K")));
        Assert.False(move.CancelsInto(FighterMove.Parse("Jab: 5P")));

        Assert.False(FighterMove.TryParse("5P", out _, out var noName));
        Assert.Equal("it needs a name and a colon first, e.g. Jab: 5P", noName);
        Assert.False(FighterMove.TryParse("Jab: 5P frames 3/2", out _, out var frames));
        Assert.Equal("frames are startup, active and recovery, e.g. frames 3/2/6", frames);
        Assert.False(FighterMove.TryParse("Jab: 5P loud", out _, out _));
        // A line that doesn't read is left out; the rest work.
        var fighter = new FightingCharacter { Moves = ["Jab: 5P", "nonsense", "Kick: 5K"] };
        Assert.Equal(["Jab", "Kick"], fighter.MoveList.Select(m => m.Name));
        Assert.Equal([("P", "Punch"), ("K", "Kick")], fighter.ButtonList);
        Assert.Equal([("L", "Light"), ("H", "Heavy")], FightingCharacter.ParseButtons("l Light, H=Heavy"));
    }

    [Fact]
    public void The_input_history_is_written_and_read_back()
    {
        var history = new InputHistory();
        foreach (var d in new[] { 5, 5, 5, 2, 3 })
            history.Push(d, 0);
        history.Push(6, 0b01);
        history.Push(6, 0b11);
        Assert.Equal("5*3 2 3 6P 6P+K", history.Write(["P", "K"]));
        var again = new InputHistory();
        again.Read("5*3 2 3 6P 6P+K", ["P", "K"]);
        Assert.Equal(7, again.Count);
        Assert.Equal(6, again.Direction(0));
        Assert.Equal(0b11, again.Presses(0));
        Assert.Equal(2, again.Direction(3));
        Assert.Equal(5, again.Direction(99));
    }

    [Fact]
    public void A_frame_data_spreads_the_clip_over_startup_active_and_recovery()
    {
        var animation = _fighter.GetComponent<Animation>()!;
        var boxes = _fighter.GetComponent<Hitboxes>()!;
        var jab = FighterMove.Parse("Jab: 5P frames 3/2/7");
        var timing = FightingCharacter.TimingOf(jab, animation, boxes);
        Assert.Equal((3, 2, 7, 12, 1, 1), (timing.Startup, timing.Active, timing.Recovery, timing.Total, timing.FirstHit, timing.LastHit));
        Assert.Equal([0, 0, 0, 1, 1, 2, 2, 2, 2, 2, 2, 2], Enumerable.Range(0, 12).Select(timing.FrameAt));
        Assert.Equal(MovePhase.Startup, timing.PhaseAt(2));
        Assert.Equal(MovePhase.Active, timing.PhaseAt(3));
        Assert.Equal(MovePhase.Recovery, timing.PhaseAt(5));

        // Left out, the clip's own: a frame each at 15 a second is 4 steps.
        var own = FightingCharacter.TimingOf(FighterMove.Parse("Jab: 5P"), animation, boxes);
        Assert.Equal((4, 4, 4, true), (own.Startup, own.Active, own.Recovery, own.StartupFromClip));
        Assert.False(FightingCharacter.TimingOf(FighterMove.Parse("Jab: 5P clip Nothing frames 2/2/2"), animation, boxes).HasClip);
    }

    [Fact]
    public void A_jab_hits_only_on_its_active_frames_and_the_fighter_is_free_after_its_recovery()
    {
        var fighter = FighterOf(_fighter);
        _fighter.Transform.Position = _dummy.Transform.Position - new Vector3(0.9f, 0, 0);
        Step(2);
        var phases = new List<string>();
        Play("5P");
        phases.Add(fighter.PhaseName);
        for (var i = 0; i < 12; i++)
        {
            Play("5");
            phases.Add(fighter.PhaseName);
        }

        Assert.Equal(["Startup", "Startup", "Startup", "Active", "Active", "Recovery", "Recovery", "Recovery", "Recovery", "Recovery", "Recovery", "Recovery", ""], phases);
        // On the head, which takes half as much again.
        Assert.Equal(27, HealthOf(_dummy));
        Assert.Equal(1, _fighter.GetComponent<Hitboxes>()!.Hits);
        Assert.True(fighter.IsFree);
    }

    [Fact]
    public void Special_moves_come_from_motions_read_as_the_fighter_faces()
    {
        Assert.Equal("Palm", Started(() => Play("2", "3", "6P", "5*40")));
        Assert.Equal("Uppercut", Started(() => Play("6", "2", "3P", "5*60")));
        Assert.Equal("Low Kick", Started(() => Play("2K", "5*40")));
        Assert.Equal("Kick", Started(() => Play("5K", "5*40")));
        Assert.Equal("Jab", Started(() => Play("6P", "5*40")));
        // A walk forward into a quarter circle makes a dragon punch too (6 2 3), but the quarter circle ended nearer the button.
        Assert.Equal("Palm", Started(() => Play("6*4", "2", "3", "6P", "5*60")));
        Assert.Equal("Uppercut", Started(() => Play("6", "5", "2", "3", "5P", "5*60")));

        // Turned round, forward is the other way: 214 on the keys is a quarter circle forward.
        _fighter.Transform.Position = new Vector3(3.5f, -0.5f, 0);
        Step(20);
        Assert.True(_fighter.GetComponent<Sprite>()!.FlipX);
        Assert.Equal("Palm", Started(() => Play("2", "1", "4P", "5*40")));
        Assert.NotEqual("Palm", Started(() => Play("2", "3", "6P", "5*40")));
    }

    [Fact]
    public void A_motion_too_slow_or_a_charge_too_short_doesnt_come_out()
    {
        Assert.Equal("Jab", Started(() => Play("2", "3*30", "6P", "5*40")));
        Assert.Equal("Kick", Started(() => Play("4*10", "6K", "5*40")));
        Assert.Equal("Rush", Started(() => Play("4*45", "6K", "5*60")));
        // Charged crouching (down-back) counts as back.
        Assert.Equal("Rush", Started(() => Play("1*45", "6K", "5*60")));
    }

    [Fact]
    public void A_press_a_few_frames_early_comes_out_as_soon_as_it_can_once()
    {
        var fighter = FighterOf(_fighter);
        var moves = new List<string>();
        fighter.MoveStarted += m => moves.Add(m.Name);

        // The kick's last steps of recovery: a press 5 steps before it ends comes out the step it does.
        Play("5K");
        var total = FightingCharacter.TimingOf(fighter.Current!, _fighter.GetComponent<Animation>(), _fighter.GetComponent<Hitboxes>()).Total;
        Play($"5*{total - 6}", "5P");
        Assert.Equal(["Kick"], moves);
        Play("5*5");
        Assert.Equal(["Kick", "Jab"], moves);
        Play("5*40");
        Assert.Equal(["Kick", "Jab"], moves);

        // Too early (more than its Buffer Frames before): nothing.
        Play("5K", $"5*{total - 12}", "5P", "5*40");
        Assert.Equal(["Kick", "Jab", "Kick"], moves);

        // The motion buffered too: a quarter circle done during the kick comes out as it ends.
        Play("5K", $"5*{total - 8}", "2", "3", "6P", "5*40");
        Assert.Equal("Palm", moves[^1]);
    }

    [Fact]
    public void A_move_cancels_into_a_special_once_it_connects_and_the_hits_count_as_a_combo()
    {
        var fighter = FighterOf(_fighter);
        var moves = new List<string>();
        fighter.MoveStarted += m => moves.Add(m.Name);

        // Out of reach: the jab whiffs, so the quarter circle during it waits for it to end.
        Play("5P", "5*5", "2", "3", "6P");
        Assert.Equal(["Jab"], moves);
        Play("5*30");
        Assert.Equal(["Jab", "Palm"], moves);

        // Up close: the jab hits, and the palm cancels it at once, landing while the dummy is still stunned.
        _fighter.Transform.Position = _dummy.Transform.Position - new Vector3(0.9f, 0, 0);
        Play("5*40");
        moves.Clear();
        Play("5P", "5*3", "2", "3", "6P");
        Assert.Equal(["Jab", "Palm"], moves);
        Play("5*14");
        Assert.Equal(2, fighter.Combo);
        // The jab on the head (3), then the palm (4).
        Assert.Equal(7, fighter.ComboDamage);
        Assert.Equal(23, HealthOf(_dummy));
        Assert.Equal(2, fighter.BestCombo);

        // Once the dummy recovers, the combo is over.
        Play("5*60");
        Assert.Equal(0, fighter.Combo);
        Assert.Equal(2, fighter.BestCombo);
    }

    [Fact]
    public void A_cancel_pressed_during_the_hit_stop_comes_out_after_it()
    {
        _fighter.GetComponent<Hitboxes>()!.HitStop = 0.1f;
        _fighter.Transform.Position = _dummy.Transform.Position - new Vector3(0.9f, 0, 0);
        Step(2);
        var moves = new List<string>();
        FighterOf(_fighter).MoveStarted += m => moves.Add(m.Name);
        // The jab lands on its fourth step and freezes the game for six: the quarter circle and P all come in the freeze.
        Play("5P", "5*3");
        Assert.True(_game.Time.IsFrozen);
        Play("2", "3", "6P", "5*30");
        Assert.Equal(["Jab", "Palm"], moves);
        Assert.Equal(2, FighterOf(_fighter).BestCombo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void A_kick_cancels_into_the_palm_whenever_in_the_hit_stop_the_button_comes(int late)
    {
        // The kick lands on its seventh step and freezes the game for four: P on each of them, and the steps after.
        _fighter.GetComponent<Hitboxes>()!.HitStop = 4 / 60f;
        _fighter.Transform.Position = _dummy.Transform.Position - new Vector3(0.7f, 0, 0);
        Step(2);
        var moves = new List<string>();
        FighterOf(_fighter).MoveStarted += m => moves.Add(m.Name);
        Play("5K", "5*3", "2", "3", $"6*{late + 1}", "6P", "5*30");
        Assert.Equal(["Kick", "Palm"], moves);
        Assert.Equal(2, FighterOf(_fighter).BestCombo);
    }

    [Fact]
    public void Hitstun_holds_a_fighter_still_and_its_buttons_do_nothing()
    {
        var dummy = FighterOf(_dummy);
        _fighter.Transform.Position = _dummy.Transform.Position - new Vector3(0.9f, 0, 0);
        Step(2);
        Play("5K", "5*8");
        Assert.True(_dummy.GetComponent<Hitboxes>()!.Hitstun > 0);
        Assert.Equal("Hurt", _dummy.GetComponent<Animation>()!.Clip);
        Assert.False(dummy.IsFree);
        dummy.Perform("Jab");
        Play("5");
        Assert.Null(dummy.Move);
        Play("5*30");
        Assert.True(dummy.IsFree);
        dummy.Perform("Jab");
        Play("5");
        Assert.Equal("Jab", dummy.Move);
    }

    [Fact]
    public void Holding_back_blocks_with_chip_damage_and_blockstun_and_lows_must_be_blocked_low()
    {
        var dummy = FighterOf(_dummy);
        dummy.AutoBlock = AutoBlock.Standing;
        _fighter.Transform.Position = _dummy.Transform.Position - new Vector3(0.9f, 0, 0);
        Step(2);
        var blocked = new List<HitInfo>();
        dummy.Blocked += blocked.Add;

        // The palm blocked standing: a quarter of its 4 damage (its chip), blockstun, no hitstun, and the palm still connected.
        Play("2", "3", "6P", "5*14");
        Assert.Single(blocked);
        Assert.Equal(1, blocked[0].Damage, 3);
        Assert.Equal(29, HealthOf(_dummy), 3);
        Assert.Equal(0, _dummy.GetComponent<Hitboxes>()!.Hitstun);
        Assert.True(dummy.Blockstun > 0);
        Assert.Equal("Block", _dummy.GetComponent<Animation>()!.Clip);
        Assert.Equal(0, _fighter.GetComponent<Hitboxes>()!.Hits);
        Play("5*40");

        // A low kick isn't blocked standing; crouching (down-back) it is, for the fighter's 10% chip.
        _fighter.Transform.Position = _dummy.Transform.Position - new Vector3(0.9f, 0, 0);
        Play("2K", "5*20");
        Assert.Single(blocked);
        Assert.Equal(27, HealthOf(_dummy), 3);
        Play("5*40");
        dummy.AutoBlock = AutoBlock.Crouching;
        _fighter.Transform.Position = _dummy.Transform.Position - new Vector3(0.9f, 0, 0);
        Play("2K", "5*8");
        Assert.Equal(2, blocked.Count);
        Assert.Equal(26.8f, HealthOf(_dummy), 3);
        Assert.True(dummy.Crouching);
        Assert.Equal("CrouchBlock", _dummy.GetComponent<Animation>()!.Clip);
    }

    [Fact]
    public void A_player_holding_back_blocks_and_an_overhead_must_be_blocked_standing()
    {
        // The dummy played by the keys this time, the fighter by Perform.
        var (fighter, dummy) = (FighterOf(_fighter), FighterOf(_dummy));
        (fighter.Controllable, dummy.Controllable) = (false, true);
        _fighter.Transform.Position = _dummy.Transform.Position - new Vector3(0.7f, 0, 0);
        Step(2);
        var blocked = 0;
        dummy.Blocked += _ => blocked++;

        // Facing left, back is to the right: holding the D key (6, as the keys are written here for one facing right).
        fighter.Perform("Kick");
        Play("6*20");
        Assert.Equal(1, blocked);
        Assert.Equal(0, _dummy.GetComponent<Hitboxes>()!.Hitstun);
        Play("5*40");

        // An overhead hits a crouching blocker, and is blocked standing.
        fighter.Moves = [.. fighter.Moves, "Overhead: 6K clip Kick frames 8/3/14 high"];
        _fighter.Transform.Position = _dummy.Transform.Position - new Vector3(0.7f, 0, 0);
        fighter.Perform("Overhead");
        Play("3*20");
        Assert.Equal(1, blocked);
        Assert.True(_dummy.GetComponent<Hitboxes>()!.Hitstun > 0 || HealthOf(_dummy) < 30);
        Play("5*60");
        _fighter.Transform.Position = _dummy.Transform.Position - new Vector3(0.7f, 0, 0);
        fighter.Perform("Overhead");
        Play("6*20");
        Assert.Equal(2, blocked);

        // Not while it's doing a move of its own.
        Play("5*40");
        var before = HealthOf(_dummy);
        _fighter.Transform.Position = _dummy.Transform.Position - new Vector3(0.7f, 0, 0);
        // Holding back but kicking as the fighter's jab comes: it's hit, and its kick is cut short.
        fighter.Perform("Jab");
        Play("6K", "6*20");
        Assert.True(HealthOf(_dummy) < before - 1);
        Assert.Equal(0, blocked - 2);
    }

    [Fact]
    public void Fighters_face_each_other_and_walk_forward_and_back()
    {
        Assert.False(_fighter.GetComponent<Sprite>()!.FlipX);
        Assert.True(_dummy.GetComponent<Sprite>()!.FlipX);
        var x = _fighter.Transform.Position.X;
        Play("6*20");
        Assert.True(_fighter.Transform.Position.X > x + 0.5f);
        Assert.Equal("Walk", _fighter.GetComponent<Animation>()!.Clip);
        x = _fighter.Transform.Position.X;
        Play("4*20");
        Assert.True(_fighter.Transform.Position.X < x - 0.4f);
        Assert.True(FighterOf(_fighter).Blocking);
        Assert.Equal("Walk", _fighter.GetComponent<Animation>()!.Clip);
        Play("2*5");
        Assert.True(FighterOf(_fighter).Crouching);
        Assert.Equal("Crouch", _fighter.GetComponent<Animation>()!.Clip);
        Play("8", "5*5");
        Assert.False(_fighter.GetComponent<Body>()!.IsGrounded);
        Assert.Equal("Air Kick", Started(() => Play("5K")));
        Play("5*90");

        // Jumped over, they turn round once they land.
        _dummy.Transform.Position = new Vector3(-4, -0.5f, 0);
        Play("5*10");
        Assert.True(_fighter.GetComponent<Sprite>()!.FlipX);
        Assert.False(_dummy.GetComponent<Sprite>()!.FlipX);
    }

    [Fact]
    public void Moves_can_come_from_a_table_and_a_trigger_can_perform_one()
    {
        var table = new Table("Moves",
            [new TableColumn("Input", ColumnType.Text), new TableColumn("Clip", ColumnType.Text), new TableColumn("Frames", ColumnType.Text), new TableColumn("Hits", ColumnType.Text)],
            [("Sweep", [TableValue.Of("22K"), TableValue.Of("LowKick"), TableValue.Of("6/3/20"), TableValue.Of("Low")])]);
        _game.Tables = new TableSet([table]);
        var fighter = FighterOf(_fighter);
        fighter.MovesTable = "Moves";
        Assert.Equal("Sweep", Started(() => Play("2", "5", "2K", "5*40")));

        fighter.Perform("Uppercut");
        Assert.Equal("Uppercut", Started(() => Play("5")));
    }

    private static string Snapshot(Scene scene) => string.Join("; ", scene.Entities.Where(e => e.GetComponent<FightingCharacter>() is not null).Select(e =>
        $"{e.Name} {e.Transform.Position.X:R},{e.Transform.Position.Y:R} {e.GetComponent<Health>()?.Current:R} {e.GetComponent<FightingCharacter>()!.Move} " +
        $"{e.GetComponent<FightingCharacter>()!.MoveFrame} {e.GetComponent<FightingCharacter>()!.Combo} {e.GetComponent<Hitboxes>()!.Hitstun} {e.GetComponent<Animation>()?.Time:R}"));

    // A short fight on the keys: walk in, jab into a palm, an uppercut, a sweep, a charge.
    private static readonly string[] Bout =
        ["6*35", "5P", "5*3", "2", "3", "6P", "5*30", "6*10", "6", "2", "3P", "5*50", "2K", "5*30", "4*45", "6K", "5*60", "6*20", "5K", "5*30"];

    [Fact]
    public void The_same_input_fights_the_same_to_the_bit_twice_and_across_a_rewind()
    {
        static string Fight()
        {
            var scene = Training(out _, out var dummy);
            var keys = new InputState();
            var game = new Game(scene, keys);
            Play(game, keys, Bout);
            Assert.True(dummy.GetComponent<Health>()!.Current < 30);
            return Snapshot(scene);
        }

        Assert.Equal(Fight(), Fight());

        // Rewound to the middle of the quarter circle, in the hit stop of the jab it cancels: the motion's start and the rest of
        // the freeze come back with it, and the palm comes out on the same step.
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var live = Training(out _, out _);
        var keys = new InputState();
        var game = new Game(live, keys);
        var timeline = new Timeline(serializer, capacity: 10, framesPerSnapshot: 1);
        Play(game, keys, "6*35", "5P", "5*3", "2", "3");
        timeline.Record(live, game.Time.Total);
        var rest = new[] { "6P", "5*30", "6*10", "6", "2", "3P", "5*50", "2K", "5*30" };
        Play(game, keys, rest);

        var restored = timeline.Restore(0);
        var replayKeys = new InputState();
        var replay = new Game(restored, replayKeys);
        Assert.True(replay.Time.IsFrozen);
        Play(replay, replayKeys, rest);
        Assert.Equal(Snapshot(live), Snapshot(restored));
        Assert.True(HealthOf(restored.Entities.Single(e => e.Name == "Dummy")) < 30);
    }

    [Fact]
    public void In_a_network_game_the_host_runs_each_players_fighter_on_their_own_keys()
    {
        static Scene Networked()
        {
            var scene = Training(out _, out _, loose: true);
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>().Player = "Fighter";
            scene.Add(settings);
            return scene;
        }

        var world = new NetWorld(Networked);
        var mine = world.Client.Net.LocalPlayer!.Avatar!;
        var onHost = world.Host.Scene.Find(mine.Id)!;
        var moves = new List<string>();
        FighterOf(onHost).MoveStarted += m => moves.Add(m.Name);
        var keys = NetWorld.Keys(world.Client);
        foreach (var held in new[] { new[] { Key.S }, [Key.S, Key.D], [Key.D, Key.U], [] })
        {
            foreach (var key in new[] { Key.S, Key.D, Key.A, Key.U })
                keys.SetKey(key, held.Contains(key));
            world.Run(1 / 60.0);
        }

        world.Run(1);
        Assert.Equal(["Palm"], moves);
        // The host's own fighter heard none of it.
        Assert.Null(FighterOf(world.Host.Scene.Entities.First(e => e.Name == "Fighter" && e.Id != mine.Id)).Move);

    }

    [Fact]
    public void A_training_dummy_the_computer_plays_stands_crouches_or_jumps_and_ignores_the_keys()
    {
        var cpu = _dummy.AddComponent<ComputerFighter>();
        cpu.Behavior = ComputerBehavior.Crouch;
        var x = _dummy.Transform.Position.X;
        Play("5*10");
        Assert.True(FighterOf(_dummy).Crouching);

        cpu.NextBehavior();
        Assert.Equal("Jump", cpu.BehaviorName);
        Play("5*10");
        Assert.False(_dummy.GetComponent<Body>()!.IsGrounded);

        cpu.NextBehavior();
        cpu.NextBehavior();
        Assert.Equal(ComputerBehavior.Stand, cpu.Behavior);
        Play("5*120");
        FighterOf(_dummy).Controllable = true;
        Play("4*30");
        Assert.Equal(x, _dummy.Transform.Position.X, 2);
        Assert.False(FighterOf(_dummy).Crouching);
    }

    [Fact]
    public void A_computer_fighter_closes_in_attacks_guards_and_follows_up_the_same_every_time()
    {
        static (string Trace, int Moves, int Blocks, float Hurt) Fight()
        {
            var scene = Training(out var fighter, out var dummy);
            var keys = new InputState();
            var game = new Game(scene, keys);
            var cpu = dummy.AddComponent<ComputerFighter>();
            (cpu.Difficulty, cpu.Seed) = (ComputerDifficulty.Hard, 12345);
            dummy.GetComponent<Health>()!.Respawn = false;
            var (moves, blocks) = (0, 0);
            FighterOf(dummy).MoveStarted += _ => moves++;
            FighterOf(dummy).Blocked += _ => blocks++;
            // The player stands its ground, jabbing now and then.
            var steps = new List<string>();
            for (var i = 0; i < 40; i++)
                steps.AddRange(["5P", "5*24"]);
            Play(game, keys, [.. steps]);
            return (Snapshot(scene), moves, blocks, 30 - HealthOf(fighter));
        }

        var first = Fight();
        Assert.True(first.Moves >= 4, $"{first.Moves} moves");
        Assert.True(first.Blocks >= 1, "It never blocked.");
        Assert.True(first.Hurt > 0, "It never hit.");
        Assert.Equal(first, Fight());
    }

    [Fact]
    public void A_computer_fighter_uses_only_the_moves_it_is_given()
    {
        var cpu = _dummy.AddComponent<ComputerFighter>();
        (cpu.UseMoves, cpu.Difficulty) = ("Low Kick, Palm", ComputerDifficulty.Hard);
        var moves = new HashSet<string>();
        FighterOf(_dummy).MoveStarted += m => moves.Add(m.Name);
        Play("5*900");
        Assert.NotEmpty(moves);
        Assert.Subset(new HashSet<string> { "Low Kick", "Palm" }, moves);
    }

    [Fact]
    public void A_fighter_knocked_out_lies_down_and_a_held_one_reads_no_keys()
    {
        _fighter.GetComponent<Health>()!.DestroyOnDeath = false;
        _fighter.GetComponent<Health>()!.Kill();
        Play("6P", "5*10");
        Assert.Equal("Hurt", _fighter.GetComponent<Animation>()!.Clip);
        Assert.Null(FighterOf(_fighter).Move);
        Assert.Equal(-1.2f, _fighter.Transform.Position.X, 1);

        _fighter.GetComponent<Health>()!.Current = 30;
        FighterOf(_fighter).Held = true;
        Play("6P", "6*20");
        Assert.Null(FighterOf(_fighter).Move);
        Assert.Equal(-1.2f, _fighter.Transform.Position.X, 1);
        FighterOf(_fighter).Held = false;
        Play("6*20");
        Assert.True(_fighter.Transform.Position.X > -1.0f);
    }

    [Fact]
    public void Fighting_makes_no_garbage_while_fighters_walk_crouch_and_do_moves()
    {
        Play("5*30");
        // Walking in, a quarter circle and the palm it makes, walking back and crouching, the keys set beforehand.
        var steps = new[] { 6, 6, 2, 3, 16, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 4, 4, 4, 4, 2, 2 };
        void Bout()
        {
            foreach (var step in steps)
            {
                var direction = step % 10;
                _keys.SetKey(Key.A, direction is 1 or 4 or 7);
                _keys.SetKey(Key.D, direction is 3 or 6 or 9);
                _keys.SetKey(Key.S, direction <= 3);
                _keys.SetKey(Key.U, step > 10);
                _game.Tick(Frame);
                _keys.EndFrame();
            }
        }

        var perStep = Allocations.PerCall(Bout, 5);
        Assert.True(perStep == 0, $"A bout of steps allocated {perStep:N0} bytes.");
    }

    [Fact]
    public void Two_players_at_one_keyboard_fight_each_on_their_side_with_no_garbage()
    {
        _fighter.AddComponent<PlayedBy>().Player = 1;
        _dummy.AddComponent<PlayedBy>().Player = 2;
        FighterOf(_dummy).Controllable = true;
        Step(2);

        // The left player's F is its punch; the right player's K is the dummy's.
        string? first = null, second = null;
        FighterOf(_fighter).MoveStarted += m => first ??= m.Name;
        FighterOf(_dummy).MoveStarted += m => second ??= m.Name;
        _keys.SetKey(Key.F, true);
        Step();
        _keys.SetKey(Key.F, false);
        Step(30);
        _keys.SetKey(Key.K, true);
        Step();
        _keys.SetKey(Key.K, false);
        Step(30);
        Assert.Equal(("Jab", "Jab"), (first, second));

        void Bout()
        {
            for (var i = 0; i < 40; i++)
            {
                _keys.SetKey(Key.D, i < 20);
                _keys.SetKey(Key.Left, i >= 20);
                _keys.SetKey(Key.F, i == 5);
                _keys.SetKey(Key.L, i == 25);
                _game.Tick(Frame);
                _keys.EndFrame();
            }
        }

        var perStep = Allocations.PerCall(Bout, 5);
        Assert.True(perStep == 0, $"A bout allocated {perStep:N0} bytes.");
    }

    [Fact]
    public void A_computer_fighter_makes_no_garbage()
    {
        var cpu = _dummy.AddComponent<ComputerFighter>();
        (cpu.Difficulty, cpu.UseMoves) = (ComputerDifficulty.Hard, "Jab, Kick, Low Kick, Palm");
        Play("5*60");
        var perStep = Allocations.PerCall(() => Step(30), 5);
        Assert.True(perStep == 0, $"The computer's steps allocated {perStep:N0} bytes.");
    }
}
