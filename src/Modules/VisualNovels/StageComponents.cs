using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.VisualNovels;

/// <summary>
/// A visual novel's stage: a background filling its <see cref="Size"/>, and the characters inside it (each a
/// <see cref="Character"/>) standing at its named <see cref="Positions"/>. The lines of a <see cref="Dialogue"/> direct it
/// with tags, done as each line is said: "[background Park fade]" changes the background, "[show Ava left happy]" brings
/// Ava on at the left looking happy (or, when she's on already, moves her there and changes her face), "[move Ava right]"
/// walks her over, "[hide Ava]" takes her off and "[hide all]" clears the stage; a tag can end with how it happens, cut, fade
/// or slide. "Ava (happy): ..." says a line with that expression. Whoever speaks is lit and the others on stage dimmed. Its
/// <see cref="AdvanceAction"/> (Space, the gamepad's A) moves the talk on as Interact and a click or tap do (finishing the
/// line being typed first).
/// </summary>
/// <remarks>
/// A dialogue directs the stage it's inside, or else the scene's first. Its Background, Show, Hide, Hide All and Direct
/// actions are there for a trigger, a wire or a script, and its Background and Speaking outputs for a text. What it shows
/// (the background, who's on, where and how they look, and transitions under way) is saved with the scene, so a rewind goes
/// back to it. In a network game each machine keeps its own stage, for its own player's reading.
/// </remarks>
[Category("Visual Novel")]
[WorksWith(typeof(Character), Note = "who stands on it, inside it")]
[WorksWith(typeof(Dialogue), Note = "whose lines direct it", Back = "a visual novel Stage its lines direct with [show], [hide], [move] and [background]")]
[ComponentName("Griff.Stage")]
[DialogueTags("show", "hide", "move", "background", "bg")]
[NetLocal]
public sealed class Stage : Component, IDrawable
{
    /// <summary>The positions a new stage starts with.</summary>
    public static IReadOnlyList<string> DefaultPositions { get; } = ["Left: -4.5", "Center: 0", "Right: 4.5"];

    /// <summary>
    /// Where characters stand, one per row: a one-word name and how far across from the stage's middle (and, after a
    /// comma, how far up), e.g. "Left: -4.5" or "FarLeft: -7, -0.5". Tags name them: "[show Ava left]".
    /// </summary>
    [Positions]
    public IReadOnlyList<string> Positions { get; set; } = DefaultPositions;

    /// <summary>
    /// The backgrounds it can show, one per row: a name and a picture ("Park: Backgrounds/park.png"), a color ("Night:
    /// #10183A"), or a color at the top running to another at the bottom ("Dusk: #F2A65A #3A2F5B"). Tags name them:
    /// "[background Park fade]".
    /// </summary>
    [Backgrounds]
    public IReadOnlyList<string> Backgrounds { get; set; } = ["Day: #8EC5FF #E8F4FF", "Night: #0B1026 #2B3A67"];

    /// <summary>The background showing, one of <see cref="Backgrounds"/>: set it here for the one the game starts with.</summary>
    [Output]
    [ChoiceOf(nameof(BackgroundNames), Noun = "one of its backgrounds (Backgrounds)")]
    public string? Background { get; set; } = "Day";

    // Its backgrounds' names, to pick Background from.
    private IReadOnlyList<string> BackgroundNames() => StageLines.Names(Backgrounds);

    /// <summary>How big the background is, in world units (as much as the camera shows, for a whole screen).</summary>
    public Vector2 Size { get; set; } = new(16, 9);

    /// <summary>Where the background draws among the rest: under the characters (0) and the dialogue box.</summary>
    public int Order { get; set; } = -100;

    /// <summary>How backgrounds change when a tag doesn't say: "[background Park]".</summary>
    [Group("Transitions")]
    public StageTransition BackgroundTransition { get; set; } = StageTransition.Fade;

    /// <summary>How characters come on and go off when a tag doesn't say: "[show Ava]".</summary>
    public StageTransition CharacterTransition { get; set; } = StageTransition.Fade;

    /// <summary>Seconds a fade or slide takes.</summary>
    public float TransitionTime { get; set; } = 0.5f;

    /// <summary>Seconds a character takes to walk to another position.</summary>
    public float MoveTime { get; set; } = 0.4f;

    /// <summary>How far a character slides in from (or out to), past their position, toward the side of the stage they're on.</summary>
    public float SlideDistance { get; set; } = 3;

    /// <summary>
    /// How much darker the others on stage go while someone on it speaks, from 0 (not at all) to 1 (black). When nobody on
    /// stage speaks (a narrator), nobody is dimmed.
    /// </summary>
    [Group("Speaking")]
    public float DimOthers { get; set; } = 0.45f;

    /// <summary>Takes the player's input: off, its Advance Action doesn't move the talk on (Interact and clicks still do).</summary>
    [Group("Controls")]
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>An input action that moves the talk on as well as Interact and a click: Jump is Space and the gamepad's A.</summary>
    [Names(NameKind.Action)]
    public string? AdvanceAction { get; set; } = "Jump";

    /// <summary>
    /// An input action that skips while it's held: lines read before go by in a moment, stopping at choices and at a line
    /// not read yet (unless the player's Skip Unread setting is on). Skip is Ctrl and the gamepad's right trigger.
    /// </summary>
    [Names(NameKind.Action)]
    public string? SkipAction { get; set; } = "Skip";

    /// <summary>
    /// An input action that turns reading on by itself on and off (see the player's Auto Speed setting): Auto is A and the
    /// gamepad's X.
    /// </summary>
    [Names(NameKind.Action)]
    public string? AutoAction { get; set; } = "Auto";

    /// <summary>The background being changed from, while a fade or slide is under way.</summary>
    [Group("State")]
    [PlainText("the background being left: the game sets it")]
    public string? LeavingBackground { get; set; }

    /// <summary>How the background is changing.</summary>
    public StageTransition Changing { get; set; }

    /// <summary>Seconds left of the background's change.</summary>
    public float ChangeLeft { get; set; }

    /// <summary>The name of whoever on the stage is speaking now, or empty.</summary>
    [DoNotSerialize]
    [Output]
    public string Speaking { get; internal set; } = "";

    /// <summary>How many characters are on it now.</summary>
    [DoNotSerialize]
    [Output]
    public int OnStage { get; internal set; }

    /// <summary>
    /// Its characters: those inside it, and, for the scene's first stage, any that aren't inside a stage at all.
    /// </summary>
    [DoNotSerialize]
    public IReadOnlyList<Character> Cast
    {
        get
        {
            if (Entity?.Scene is not { } scene)
                return Entity is null ? [] : [.. Entity.SelfAndDescendants().Select(e => e.GetComponent<Character>()).OfType<Character>()];
            var first = scene.AllNow<Stage>().FirstOrDefault() == this;
            return [.. scene.AllNow<Character>().Where(c => c.Entity is { } e && (StageOf(e) == this || (first && StageOf(e) is null)))];
        }
    }

    /// <summary>The character called <paramref name="name"/> (their speaker's name, or their entity's), or null.</summary>
    public Character? CharacterNamed(string? name) => Cast.FirstOrDefault(c => StageLines.Same(c.Name, name));

    /// <summary>Where position <paramref name="name"/> is from the stage's middle, or null when it has none called that.</summary>
    public Vector2? PositionOf(string? name) =>
        StageLines.Spots(Positions).FirstOrDefault(s => StageLines.Same(s.Name, name)) is { Name: not null } spot ? spot.At : null;

    /// <summary>The stage <paramref name="entity"/> is inside (or is), or null.</summary>
    public static Stage? StageOf(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Stage>() is { } stage)
                return stage;
        }

        return null;
    }

    /// <summary>
    /// Changes the background to <paramref name="name"/>, one of <see cref="Backgrounds"/>, as "[background Park]" does,
    /// with its <see cref="BackgroundTransition"/>.
    /// </summary>
    [Action("Background")]
    public void ChangeBackground(string name) => StageDirector.Background(this, name, null);

    /// <summary>Brings the character called that on, as "[show Ava]" does: where they were last, or in the middle.</summary>
    [Action]
    public void Show(string character) => StageDirector.Do(this, new StageDirection(DirectionKind.Show, character));

    /// <summary>Takes the character called that off, as "[hide Ava]" does.</summary>
    [Action]
    public void Hide(string character) => StageDirector.Do(this, new StageDirection(DirectionKind.Hide, character));

    /// <summary>Takes everyone off, as "[hide all]" does.</summary>
    [Action]
    public void HideAll() => StageDirector.Do(this, new StageDirection(DirectionKind.Hide, "all"));

    /// <summary>Does a direction written as a dialogue tag says it, without the brackets: "show Ava left happy", "background Night fade".</summary>
    [Action]
    public void Direct(string direction)
    {
        var text = direction.Trim().TrimStart('[').TrimEnd(']').Trim();
        var space = text.IndexOf(' ');
        if (space > 0 && StageLines.Read(text[..space], text[(space + 1)..], this) is { } read)
            StageDirector.Do(this, read);
    }

    int? IDrawable.DrawOrder => Order;

    int IDrawable.DrawStep => DrawSteps.Sprite;

    void IDrawable.Draw(Drawing drawing) => StageDirector.DrawBackground(this, drawing);
}

/// <summary>
/// Someone who appears on a visual novel's <see cref="Stage"/>, inside it: on stage while their entity is shown, standing
/// at one of the stage's positions, looking as their <see cref="Expression"/> says. Each expression is a picture for their
/// <see cref="Sprite"/> or a clip their <see cref="Animation"/> plays. Their name is their <see cref="Speaker"/>'s (or
/// their entity's), which the stage's tags and lines use: "[show Ava left happy]", "Ava (sad): Oh." While they speak
/// they're lit, and the others on stage dimmed.
/// </summary>
[Category("Visual Novel")]
[Requires(typeof(Sprite), Note = "how they look")]
[WorksWith(typeof(Speaker), Note = "their name, for the lines they say")]
[WorksWith(typeof(Animation), Note = "for expressions that are clips")]
[ComponentName("Griff.Character")]
public sealed class Character : Component, IDrawEffect
{
    private string? _expression;
    private string? _position;

    /// <summary>
    /// How they can look, one per row: a one-word name and a picture for their sprite ("Happy: Characters/ava-happy.png"),
    /// or the name of a clip their animation plays ("Laugh: Laugh").
    /// </summary>
    [Expressions]
    public IReadOnlyList<string> Expressions { get; set; } = [];

    /// <summary>How they look now, one of <see cref="Expressions"/>: their sprite shows it as soon as it's set.</summary>
    [Output]
    [ChoiceOf(nameof(ExpressionNames), Noun = "one of their expressions (Expressions)")]
    public string? Expression
    {
        get => _expression;
        set
        {
            if (string.Equals(_expression, value, StringComparison.Ordinal))
                return;
            _expression = value;
            ShowExpression();
        }
    }

    /// <summary>Where on the stage they stand, one of its positions: set here, they stand there.</summary>
    [Output]
    [ChoiceOf(nameof(PositionNames), Noun = "one of their stage's positions")]
    public string? Position
    {
        get => _position;
        set
        {
            if (string.Equals(_position, value, StringComparison.Ordinal))
                return;
            _position = value;
            if (MoveLeft <= 0 && Entity is { } entity && (Stage.StageOf(entity) ?? entity.Scene?.AllNow<Stage>().FirstOrDefault()) is { } stage &&
                stage.PositionOf(value) is { } spot)
                StageDirector.Place(this, stage, spot);
        }
    }

    // Their expressions' names and their stage's positions', to pick Expression and Position from.
    private IReadOnlyList<string> ExpressionNames() => StageLines.Names(Expressions);

    private IReadOnlyList<string> PositionNames() => Entity is { } entity && (Stage.StageOf(entity) ?? entity.Scene?.AllNow<Stage>().FirstOrDefault()) is { } stage
        ? [.. StageLines.Spots(stage.Positions).Select(s => s.Name)]
        : [];

    /// <summary>How much of them shows, from 0 (not at all) to 1, as they fade in and out.</summary>
    [Group("State")]
    public float Shown { get; set; } = 1;

    /// <summary>True while they're going off: they're hidden once they've faded or slid away.</summary>
    public bool Leaving { get; set; }

    /// <summary>How lit they are, from 0 to 1: lower while someone else on stage is speaking.</summary>
    public float Brightness { get; set; } = 1;

    /// <summary>Where a walk or slide started, from the stage's middle.</summary>
    public Vector2 MoveFrom { get; set; }

    /// <summary>Where a walk or slide ends, from the stage's middle.</summary>
    public Vector2 MoveTo { get; set; }

    /// <summary>Seconds left of a walk or slide.</summary>
    public float MoveLeft { get; set; }

    /// <summary>Seconds the walk or slide takes in all.</summary>
    public float MoveTime { get; set; }

    /// <summary>Seconds the fade in or out takes in all (0 for none under way).</summary>
    public float FadeTime { get; set; }

    /// <summary>Their name: their <see cref="Speaker"/>'s, or else their entity's.</summary>
    public string Name => Entity?.GetComponent<Speaker>() is { } speaker ? speaker.ShownName : Entity?.Name ?? "";

    /// <summary>True while they're on stage (and not going off).</summary>
    [Output("On Stage")]
    public bool IsOnStage => Entity is { IsEnabled: true } && !Leaving;

    /// <summary>Brings them on, as "[show Ava]" does: where they were last, or in the middle.</summary>
    [Action]
    public void Show() => OwnStage()?.Show(Name);

    /// <summary>Takes them off, as "[hide Ava]" does.</summary>
    [Action]
    public void Hide() => OwnStage()?.Hide(Name);

    /// <summary>Makes them look as <paramref name="expression"/> says, one of their <see cref="Expressions"/>, e.g. "Happy".</summary>
    [Action]
    public void Look(string expression) => Expression = expression;

    /// <summary>Stands them at <paramref name="position"/>, one of their stage's positions, e.g. "Left".</summary>
    [Action]
    public void Stand(string position) => Position = position;

    // The stage they're on: the one they're inside, else the scene's first.
    private Stage? OwnStage() => Entity is { } entity ? Stage.StageOf(entity) ?? entity.Scene?.AllNow<Stage>().FirstOrDefault() : null;

    // Shows the expression on their sprite (a picture) or animation (a clip).
    private void ShowExpression()
    {
        if (Entity is not { } entity || StageLines.Faces(Expressions).FirstOrDefault(f => StageLines.Same(f.Name, _expression)) is not { Name: not null } face)
            return;
        if (face.IsImage)
        {
            if (entity.GetComponent<Sprite>() is { } sprite)
                sprite.Image = face.Value;
        }
        else if (entity.GetComponent<Animation>() is { } animation && animation.HasClip(face.Value))
        {
            animation.Play(face.Value);
        }
    }

    // Shown again with what it says now: a change to Expressions shows it as it is now.
    internal void Refresh() => ShowExpression();

    Color IDrawEffect.Tint(IDrawable drawn, Color color)
    {
        if (Shown >= 1 && Brightness >= 1)
            return color;
        var lit = Math.Clamp(Brightness, 0, 1);
        return new Color(color.R * lit, color.G * lit, color.B * lit, color.A * Math.Clamp(Shown, 0, 1));
    }
}
