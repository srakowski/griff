using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.VisualNovels;

/// <summary>
/// Runs visual novels' stages with no code: does the directions on dialogue lines as they're said ("[show Ava left
/// happy]"), shows each line's speaker with the expression they say it with, lit while the others dim, plays fades,
/// slides and walks on, and moves the talk on for the stage's Advance Action. A stage plays on in real
/// time, so it works while a dialogue pauses the game, and holds still under a pause menu or in a paused group.
/// </summary>
internal sealed class VisualNovelsSystem : GameSystem
{
    // The talk as it stood before the dialogues' step: the advance action only moves it on if Interact, a click or a choice
    // didn't already.
    private (Dialogue? Dialogue, int Line, bool Typing) _before;

    // Stages seen in this scene: on first sight their characters stand at their positions.
    private readonly HashSet<Stage> _seen = [];

    public override string Name => "Stages";

    protected override void SceneChanged()
    {
        _seen.Clear();
        _before = default;
    }

    /// <summary>Does one of the stage's tags as its line is said, on the stage the dialogue directs.</summary>
    internal void Direct(Dialogue dialogue, string word, string args)
    {
        if (StageFor(dialogue) is not { } stage)
        {
            Game.Warn($"\"[{word} {args}]\" in the dialogue on \"{dialogue.Entity?.Name}\" has no stage to direct: add a Stage (Entity > Genres > Visual Novels > Stage).");
            return;
        }

        if (StageLines.Read(word, args, stage) is not { } direction)
            return;
        foreach (var unknown in direction.Unknown)
        {
            Game.Warn(direction.Kind == DirectionKind.Background
                ? $"The stage \"{stage.Entity!.Name}\" has no background called \"{unknown}\" (in \"[{word} {args}]\"): its Backgrounds are {List(StageLines.Names(stage.Backgrounds))}."
                : $"\"{unknown}\" in \"[{word} {args}]\" isn't one of {CastNote(stage, direction.Who)}.");
        }

        StageDirector.Do(stage, direction);
    }

    // Before the dialogues' step.
    internal void Note()
    {
        _before = Game.Talking is { } talking ? (talking, talking.Line, Game.IsTyping(talking)) : default;
    }

    // After the dialogues' step.
    internal void Run()
    {
        var stages = Game.Showing<Stage>();
        if (stages.Count == 0)
            return;

        var talking = Game.Talking;
        var paused = Game.PausedThisFrame;
        foreach (var stage in stages)
        {
            var cast = stage.Cast;
            if (_seen.Add(stage))
                Arrange(stage, cast);
            if (paused || stage.Entity!.IsPaused)
                continue;

            var directed = talking is not null && StageFor(talking) == stage;
            var speaking = directed && talking!.Speaker is { } speaker
                ? cast.FirstOrDefault(c => c.IsOnStage && (c.Entity == speaker.Entity || StageLines.Same(c.Name, speaker.ShownName)))
                : null;
            if (speaking is not null && talking!.Expression is { Length: > 0 } face)
                speaking.Expression = Known(speaking, face);
            StageDirector.Step(stage, cast, speaking, Game.Time.UnscaledDelta);

            if (directed && stage.Controllable && Advances(stage))
                Game.AdvanceDialogue(talking!);
        }
    }

    // The advance action moves the talk on, unless Interact, a click or a choice already did this frame; with choices showing
    // it only finishes the line typing out (Interact or a click on one picks them).
    private bool Advances(Stage stage)
    {
        if (Game.Talking is not { } talking || _before.Dialogue != talking || _before.Line != talking.Line ||
            _before.Typing != Game.IsTyping(talking))
            return false;
        if (talking.Choices.Count > 0 && !_before.Typing)
            return false;
        return stage.AdvanceAction is { Length: > 0 } action && Game.Input.WasPressed(action.Trim());
    }

    // As a stage is first seen: those on it stand at their positions (unless partway through a walk) looking as they should.
    private static void Arrange(Stage stage, IReadOnlyList<Character> cast)
    {
        foreach (var character in cast)
        {
            if (character.MoveLeft <= 0 && stage.PositionOf(character.Position) is { } spot)
                StageDirector.Place(character, stage, spot);
            character.Refresh();
        }
    }

    /// <summary>The stage <paramref name="dialogue"/> directs: the one it's inside, or else the scene's first showing.</summary>
    internal Stage? StageFor(Dialogue dialogue) =>
        (dialogue.Entity is { } entity ? Stage.StageOf(entity) : null) ?? Game.Showing<Stage>().FirstOrDefault();

    // The expression as the character names it ("happy" is their "Happy").
    private static string Known(Character character, string face) =>
        StageLines.Names(character.Expressions).FirstOrDefault(n => StageLines.Same(n, face)) ?? face;

    private static string CastNote(Stage stage, string? who)
    {
        var character = stage.CharacterNamed(who);
        var parts = new List<string> { $"the stage's characters ({List(stage.Cast.Select(c => c.Name).ToList())})" };
        parts.Add($"its positions ({List(StageLines.Names(stage.Positions))})");
        if (character is not null)
            parts.Add($"{character.Name}'s expressions ({List(StageLines.Names(character.Expressions))})");
        return string.Join(", ", parts) + " or cut, fade or slide";
    }

    private static string List(IReadOnlyList<string> names) => names.Count == 0 ? "none" : string.Join(", ", names);
}
