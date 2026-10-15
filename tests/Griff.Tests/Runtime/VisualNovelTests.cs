using System.Numerics;
using Griff.Editing;
using Griff.Input;
using Griff.Modules.VisualNovels;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// Visual novels: a stage's backgrounds and characters directed by the lines of a dialogue ("[show Ava left happy]",
/// "[background Park fade]", "Ava (sad): ..."), fades, slides and walks, the speaker lit and the others dimmed, clicks,
/// taps, keys and the gamepad moving the talk on, focus, rewinds, the editor's reading of lines, and Create Stage.
/// </summary>
public sealed class VisualNovelTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly List<string> _warnings = [];

    public VisualNovelTests()
    {
        _game = new Game(_scene, _input);
        _game.Warning += (_, message) => _warnings.Add(message);
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    // The first frame starts the talk; a press on that frame is the one that started it, so the next is the first to read on.
    private void Start() => Run(2 * Frame);

    private void Click()
    {
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(Frame);
    }

    private void Press(Key key)
    {
        _input.SetKey(key, true);
        Run(Frame);
        _input.SetKey(key, false);
        Run(Frame);
    }

    // A stage with Ava (Neutral, Happy, Sad) and Ben (Neutral, Laugh as a clip) off stage, a box, and a dialogue on the
    // stage that starts with the scene, saying `lines`.
    private (Stage Stage, Dialogue Dialogue, Text Text) Build(params string[] lines)
    {
        var holder = new Entity("Stage");
        var stage = holder.AddComponent<Stage>();
        stage.Backgrounds = ["Park: Stage/park.png", "Room: Stage/room.png", "Night: #000000 #202040"];
        stage.Background = "Park";
        var dialogue = holder.AddComponent<Dialogue>();
        (dialogue.Start, dialogue.PauseGame, dialogue.Lines) = (DialogueStart.SceneStart, false, lines);
        _scene.Add(holder);

        var ava = new Entity("Ava");
        ava.AddComponent<Speaker>();
        ava.AddComponent<Sprite>().Image = "ava-neutral.png";
        var avaCharacter = ava.AddComponent<Character>();
        avaCharacter.Expressions = ["Neutral: ava-neutral.png", "Happy: ava-happy.png", "Sad: ava-sad.png"];
        ava.IsEnabled = false;
        _scene.Add(ava, holder);

        var ben = new Entity("Ben");
        ben.AddComponent<Speaker>().Name = "Ben";
        ben.AddComponent<Sprite>().Image = "ben.png";
        ben.AddComponent<Animation>().Clips = "Idle 0-1, Laugh 2-3";
        ben.AddComponent<Character>().Expressions = ["Neutral: Idle", "Laugh: Laugh"];
        ben.IsEnabled = false;
        _scene.Add(ben, holder);

        var box = new Entity("Dialogue Box");
        box.AddComponent<DialogueBox>();
        var text = box.AddComponent<Text>();
        box.AddComponent<Typewriter>().Speed = 0;
        _scene.Add(box);
        return (stage, dialogue, text);
    }

    private Character Cast(string name) => _scene.Entities.Single(e => e.Name == name).GetComponent<Character>()!;

    private float X(string name) => _scene.Entities.Single(e => e.Name == name).Transform.WorldPosition.X;

    [Fact]
    public void A_dialogue_that_starts_with_the_scene_needs_no_collider_and_starts_by_itself()
    {
        var (_, dialogue, text) = Build("Once upon a time.");
        Assert.Empty(ComponentCatalog.Unmet(ComponentRegistry.CreateDefault(), dialogue));

        Start();
        Assert.True(dialogue.IsTalking);
        Assert.Equal("Once upon a time.", text.Value);

        Click();
        Assert.True(dialogue.IsDone);
        Run(0.5f);
        Assert.False(dialogue.IsTalking);
    }

    [Fact]
    public void Show_brings_a_character_on_at_a_position_looking_as_it_says_fading_in()
    {
        var (stage, _, _) = Build("[show Ava left happy] Ava: Hello!");
        Start();
        var ava = Cast("Ava");
        Assert.True(ava.Entity!.IsEnabled);
        Assert.Equal("Left", ava.Position);
        Assert.Equal("Happy", ava.Expression);
        Assert.Equal("ava-happy.png", ava.Entity.GetComponent<Sprite>()!.Image);
        Assert.Equal(-4.5f, X("Ava"), 3);
        Assert.InRange(ava.Shown, 0, 0.1f);

        Run(stage.TransitionTime);
        Assert.Equal(1, ava.Shown);
        Assert.Equal(1, stage.OnStage);
    }

    [Fact]
    public void A_slide_brings_them_in_from_their_side_and_hide_takes_them_off()
    {
        var (stage, _, _) = Build("[show Ben right slide] Ben: Hi.", "[hide Ben] Gone.", "[show Ava at center, sad cut] Ava: Oh.");
        Start();
        Assert.InRange(X("Ben"), 4.5f + 2, 4.5f + stage.SlideDistance);
        Run(stage.TransitionTime);
        Assert.Equal(4.5f, X("Ben"), 3);

        Click();
        var ben = Cast("Ben");
        Assert.True(ben.Leaving);
        Assert.True(ben.Entity!.IsEnabled);
        Run(stage.TransitionTime + 0.05f);
        Assert.False(ben.Entity.IsEnabled);
        Assert.False(ben.Leaving);

        Click();
        var ava = Cast("Ava");
        Assert.Equal(1, ava.Shown);
        Assert.Equal(0, X("Ava"), 3);
        Assert.Equal("Sad", ava.Expression);
    }

    [Fact]
    public void Move_walks_them_over_and_a_line_said_with_an_expression_changes_their_face()
    {
        var (stage, _, _) = Build("[show Ava left cut] Ava: Hi.", "[move Ava right] Ava (sad): I'll go.", "Ava (HAPPY): Back again!");
        Start();
        Click();
        Run(stage.MoveTime / 2);
        Assert.InRange(X("Ava"), -4, 4);
        Run(stage.MoveTime);
        Assert.Equal(4.5f, X("Ava"), 3);
        Assert.Equal("Right", Cast("Ava").Position);
        Assert.Equal("Sad", Cast("Ava").Expression);

        Click();
        Assert.Equal("Happy", Cast("Ava").Expression);
        Assert.Equal("ava-happy.png", Cast("Ava").Entity!.GetComponent<Sprite>()!.Image);
    }

    [Fact]
    public void An_expression_can_be_an_animation_clip()
    {
        Build("[show Ben center laugh cut] Ben: Ha!");
        Start();
        Assert.Equal("Laugh", Cast("Ben").Entity!.GetComponent<Animation>()!.Clip);
    }

    [Fact]
    public void The_speaker_is_lit_and_the_others_dimmed_and_nobody_is_for_a_narrator()
    {
        var (stage, _, _) = Build("[show Ava left cut] [show Ben right cut] Ava: Me first.", "Ben: Now me.", "The wind blows.");
        Run(0.5f);
        Assert.Equal(1, Cast("Ava").Brightness);
        Assert.Equal(1 - stage.DimOthers, Cast("Ben").Brightness, 3);
        Assert.Equal("Ava", stage.Speaking);

        Click();
        Run(0.5f);
        Assert.Equal(1, Cast("Ben").Brightness);
        Assert.Equal(1 - stage.DimOthers, Cast("Ava").Brightness, 3);

        Click();
        Run(0.5f);
        Assert.Equal(1, Cast("Ava").Brightness);
        Assert.Equal(1, Cast("Ben").Brightness);
        Assert.Equal("", stage.Speaking);
    }

    [Fact]
    public void Backgrounds_change_with_a_fade_a_slide_or_a_cut()
    {
        var (stage, _, _) = Build("[background Room] One.", "[background night slide] Two.", "[bg Park cut] Three.");
        Start();
        Assert.Equal("Room", stage.Background);
        Assert.Equal("Park", stage.LeavingBackground);
        Assert.Equal(StageTransition.Fade, stage.Changing);
        Run(stage.TransitionTime);
        Assert.Null(stage.LeavingBackground);

        Click();
        Assert.Equal("Night", stage.Background);
        Assert.Equal(StageTransition.Slide, stage.Changing);
        Run(stage.TransitionTime);

        Click();
        Assert.Equal("Park", stage.Background);
        Assert.Null(stage.LeavingBackground);
    }

    [Fact]
    public void Clicks_keys_and_the_gamepad_move_the_talk_on_finishing_the_line_first()
    {
        var (_, dialogue, text) = Build("A long first line that types out slowly.", "Second.", "Third.", "Fourth.");
        var typewriter = text.Entity!.GetComponent<Typewriter>()!;
        typewriter.Speed = 5;
        Start();
        Assert.False(typewriter.IsDone);

        Click();
        Assert.True(typewriter.IsDone);
        Assert.Equal(0, dialogue.Line);
        Click();
        Assert.Equal(1, dialogue.Line);

        typewriter.Speed = 0;
        Run(Frame);
        Press(Key.Space);
        Assert.Equal(2, dialogue.Line);

        _input.SetGamepad([GamepadButton.A], Vector2.Zero);
        Run(Frame);
        _input.SetGamepad([], Vector2.Zero);
        Run(Frame);
        Assert.Equal(3, dialogue.Line);

        Press(Key.Enter);
        Assert.True(dialogue.IsDone);
    }

    [Fact]
    public void A_click_off_the_choices_picks_nothing_and_without_focus_the_advance_action_does_nothing()
    {
        var (stage, dialogue, _) = Build("Which way?", "> Left", "> Right", "Done.");
        _input.MouseWorld = new Vector2(50, 50);
        Start();
        Click();
        Assert.Equal(2, dialogue.Choices.Count);
        Assert.True(dialogue.IsTalking);

        Press(Key.Enter);
        Assert.Equal("Done.", dialogue.Said);

        stage.Controllable = false;
        Press(Key.Space);
        Assert.Equal("Done.", dialogue.Said);
        Assert.False(dialogue.IsDone);
    }

    [Fact]
    public void Hide_all_clears_the_stage_and_the_actions_direct_it_too()
    {
        var (stage, _, _) = Build("[show Ava left cut] [show Ben right cut] Both.", "[hide all cut] Nobody.");
        Start();
        Assert.Equal(2, stage.OnStage);
        Click();
        Run(Frame);
        Assert.Equal(0, stage.OnStage);

        stage.Direct("show Ben center laugh cut");
        stage.ChangeBackground("Night");
        Run(stage.TransitionTime + Frame);
        Assert.True(Cast("Ben").IsOnStage);
        Assert.Equal("Night", stage.Background);
        stage.Hide("Ben");
        Run(stage.TransitionTime + Frame);
        Assert.False(Cast("Ben").Entity!.IsEnabled);
        Assert.Contains(ComponentActions.Of(typeof(Stage)), a => a.Name == "Direct");
        Assert.Contains(ComponentOutputs.Of(typeof(Stage)), o => o.Name == "Background");
    }

    [Fact]
    public void Words_a_stage_doesnt_know_are_warned_about()
    {
        Build("[show Ava lft grumpy] [background Moon] Hm.");
        Start();
        Assert.Contains(_warnings, w => w.Contains("\"lft\"") && w.Contains("Left, Center, Right"));
        Assert.Contains(_warnings, w => w.Contains("\"grumpy\""));
        Assert.Contains(_warnings, w => w.Contains("no background called \"Moon\""));
    }

    [Fact]
    public void A_pause_menu_holds_the_stage_still()
    {
        var (stage, _, _) = Build("[show Ava left] Hi.");
        var menu = new Entity("Pause Menu");
        menu.AddComponent<Panel>().OpenOn = "Pause";
        _scene.Add(menu);
        Start();
        var shown = Cast("Ava").Shown;
        Press(Key.Escape);
        Run(stage.TransitionTime);
        Assert.Equal(shown, Cast("Ava").Shown, 2);
    }

    [Fact]
    public void Where_the_talk_and_the_stage_are_is_saved_so_a_rewind_lands_in_the_middle_of_both()
    {
        var (stage, dialogue, _) = Build("[show Ava left cut] Ava: One.", "[send Twice] [background Night] [move Ava right] Ava (sad): Two.", "Three.");
        var sent = 0;
        Start();
        Click();
        Run(0.1f);
        Assert.Equal(1, dialogue.Line);
        Assert.True(stage.ChangeLeft > 0);
        Assert.True(Cast("Ava").MoveLeft > 0);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var copy = serializer.LoadFromString(serializer.SaveToString(_scene));
        var input = new InputState();
        var game = new Game(copy, input);
        game.On("Twice", _ => sent++);
        game.Tick(Frame);
        var talk = copy.Entities.Single(e => e.Name == "Stage").GetComponent<Dialogue>()!;
        var again = copy.Entities.Single(e => e.Name == "Stage").GetComponent<Stage>()!;
        var ava = copy.Entities.Single(e => e.Name == "Ava").GetComponent<Character>()!;
        Assert.True(talk.IsTalking);
        Assert.Same(talk, game.Talking);
        Assert.Equal(1, talk.Line);
        Assert.Equal("Two.", talk.Said);
        Assert.Equal(0, sent);
        Assert.Equal("Night", again.Background);
        Assert.Equal("Park", again.LeavingBackground);
        Assert.Equal("Sad", ava.Expression);
        Assert.True(ava.MoveLeft > 0);
        Assert.InRange(ava.Entity!.Transform.WorldPosition.X, -4.4f, 4.4f);

        for (var i = 0; i < 60; i++)
        {
            game.Tick(Frame);
            input.EndFrame();
        }

        Assert.Equal(4.5f, ava.Entity.Transform.WorldPosition.X, 3);
        Assert.Null(again.LeavingBackground);
        input.SetMouseButton(MouseButton.Left, true);
        game.Tick(Frame);
        Assert.Equal("Three.", talk.Said);
    }

    [Fact]
    public void A_finished_scene_start_dialogue_isnt_said_again_after_a_rewind()
    {
        var (_, dialogue, _) = Build("Only once.");
        Start();
        Click();
        Assert.True(dialogue.IsDone);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var copy = serializer.LoadFromString(serializer.SaveToString(_scene));
        var game = new Game(copy, new InputState());
        game.Tick(Frame);
        Assert.Null(game.Talking);
    }

    [Fact]
    public void Long_lines_wrap_in_a_box_that_says_how_wide_it_is()
    {
        var (_, _, text) = Build("This is a rather long line that goes on and on and certainly won't fit on one row of the box.");
        var box = text.Entity!.GetComponent<DialogueBox>()!;
        box.WrapWidth = 10;
        text.Size = 0.5f;
        Start();
        var rows = text.Value!.Split('\n');
        Assert.True(rows.Length >= 3);
        Assert.All(rows, r => Assert.True(r.Length <= 40, r));
        Assert.Equal("This is a rather long line that goes on and on and certainly won't fit on one row of the box.", string.Join(' ', rows));
    }

    // ----- Reading lines, for the editor and agents -----

    [Fact]
    public void A_line_comes_apart_into_its_tags_speaker_expression_and_words_and_back()
    {
        var parts = StageLines.Split("[if Met] [show Ava left happy fade] Ava (happy): Hello again!");
        Assert.Equal(["[if Met]", "[show Ava left happy fade]"], parts.Tags);
        Assert.Equal("Ava", parts.Speaker);
        Assert.Equal("happy", parts.Expression);
        Assert.Equal("Hello again!", parts.Text);
        Assert.True(parts.IsLine);
        Assert.Equal("[if Met] [show Ava left happy fade] Ava (happy): Hello again!", StageLines.Join(parts));

        Assert.Equal("Ben: Hi.", StageLines.Join(StageLines.Split("Ava: Hi.") with { Speaker = "Ben" }));
        Assert.Equal("[bg Park] Hi.", StageLines.Join(StageLines.Split("Hi.") with { Tags = ["[bg Park]"] }));
        Assert.False(StageLines.Split("# Shop").IsLine);
        Assert.Equal("# Shop", StageLines.Join(StageLines.Split("# Shop")));
        Assert.Equal("> [if Rich] Buy it -> Shop", StageLines.Join(StageLines.Split("> [if Rich] Buy it -> Shop")));
        Assert.Equal("[sighs] Fine.", StageLines.Split("[sighs] Fine.").Text);
    }

    [Fact]
    public void A_direction_is_read_against_the_stage_with_little_words_and_any_order()
    {
        var (stage, _, _) = Build("Hi.");
        var show = StageLines.Read("show", "ava at LEFT, with happy, slide", stage)!;
        Assert.Equal((DirectionKind.Show, "Ava", "Left", "Happy", (StageTransition?)StageTransition.Slide), (show.Kind, show.Who, show.Position, show.Expression, show.Transition));
        Assert.Empty(show.Unknown);
        Assert.Equal("[show Ava Left Happy slide]", show.ToTag());
        Assert.Equal("Show Ava at Left, Happy (slide)", show.Describe());
        Assert.Equal("Background Night (fade)", StageLines.Read("bg", "night fade", stage)!.Describe());
        Assert.True(StageLines.Read("hide", "everyone", stage)!.IsEveryone);
        Assert.Null(StageLines.Read("say", "Ava", stage));
    }

    [Fact]
    public void The_stage_at_a_line_is_read_top_to_bottom_for_the_preview()
    {
        var (stage, dialogue, _) = Build(
            "[background Room] Morning.",
            "[show Ava left happy] Ava: Hi!",
            "[show Ben right] Ben (laugh): Ha!",
            "[hide Ava] [show Ben center lft] Ben: Bye.");

        var start = StageLines.At(stage, dialogue, 0);
        Assert.Equal("Room", start.Background);
        Assert.All(start.Cast, c => Assert.False(c.Shown));
        Assert.Null(start.Speaker);
        Assert.Equal("Room. Nobody on stage. Says: Morning.", start.Describe());

        var third = StageLines.At(stage, dialogue, 2);
        Assert.Equal("Ben", third.Speaker);
        Assert.Equal("Room. Ava at Left, Happy, dimmed; Ben at Right, Laugh, speaking. Says: Ben: Ha!", third.Describe());

        var last = StageLines.At(stage, dialogue, 3);
        Assert.False(last.Cast.Single(c => c.Name == "Ava").Shown);
        Assert.Equal("Center", last.Cast.Single(c => c.Name == "Ben").Position);
        Assert.Contains(last.Problems, p => p.Contains("\"lft\""));
    }

    // ----- Create Stage -----

    [Fact]
    public void Create_Stage_makes_a_stage_that_plays_through_its_script()
    {
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>().Size = 5;
        _scene.Add(camera);
        var preset = new StagePreset();
        var holder = new Entity(preset.EntityName(_scene));
        preset.Configure(holder, _scene);
        _scene.Add(holder);
        foreach (var child in preset.CreateChildren(_scene))
            _scene.Add(child, holder);
        foreach (var edit in preset.EditScene(_scene))
            edit.Apply();
        Assert.Contains(StageArt.Park, preset.Files().Keys);
        Assert.Equal(8, preset.Files().Count);

        var stage = holder.GetComponent<Stage>()!;
        var dialogue = holder.GetComponent<Dialogue>()!;
        Assert.Equal(new Vector2(160 / 9f, 10), stage.Size);
        Start();
        Assert.True(dialogue.IsTalking);
        foreach (var row in Enumerable.Range(0, dialogue.Lines.Count))
        {
            Assert.Empty(StageLines.At(stage, dialogue, row).Problems);
        }

        // Read on as a player does, the mouse up in the sky, taking the second choice: home for tea.
        _input.MouseWorld = new Vector2(0, 3);
        Run(1.5f);
        for (var i = 0; i < 5; i++)
        {
            Click();
            Run(2.5f);
        }

        // The choices wait for the line to type out.
        Assert.Equal(2, dialogue.Choices.Count);
        Run(1.5f);
        Press(Key.D2);
        Assert.Equal(13, dialogue.Line);
        for (var i = 0; i < 40 && dialogue.IsTalking; i++)
        {
            if (dialogue.Choices.Count > 0)
                Press(Key.D2);
            else
                Click();
            Run(0.3f);
        }

        Assert.True(dialogue.IsDone);
        Assert.Equal("Room", stage.Background);
        Assert.True(Cast("Ava").IsOnStage);
        Assert.Equal("Center", Cast("Ava").Position);
        Assert.True(Cast("Ben").IsOnStage);
        Assert.Empty(_warnings);
    }
}
