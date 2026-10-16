using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.VisualNovels;

/// <summary>
/// What directions do to a stage and its characters, and how they play out step by step: everything it changes is a saved
/// property of the <see cref="Stage"/> or a <see cref="Character"/>, so a rewind lands in the middle of a fade as it was.
/// </summary>
internal static class StageDirector
{
    // Strips of a background running from one color to another.
    private const int Bands = 32;

    // Seconds the speaker takes to light up and the others to dim.
    private const float DimTime = 0.15f;

    /// <summary>Does <paramref name="direction"/> on <paramref name="stage"/>, with the transitions it (or the stage) says.</summary>
    public static void Do(Stage stage, StageDirection direction)
    {
        switch (direction.Kind)
        {
            case DirectionKind.Background:
                Background(stage, direction.Background, direction.Transition);
                return;
            case DirectionKind.Hide when direction.IsEveryone:
                foreach (var one in stage.Cast.Where(c => c.IsOnStage))
                    Hide(stage, one, direction.Transition);
                return;
        }

        if (stage.CharacterNamed(direction.Who) is not { } character)
            return;
        switch (direction.Kind)
        {
            case DirectionKind.Hide:
                Hide(stage, character, direction.Transition);
                break;
            case DirectionKind.Move:
                if (direction.Expression is { Length: > 0 } face)
                    character.Expression = face;
                if (direction.Position is { Length: > 0 } to)
                    Move(stage, character, to, direction.Transition);
                break;
            default:
                Show(stage, character, direction.Position, direction.Expression, direction.Transition);
                break;
        }
    }

    /// <summary>Changes the background to <paramref name="name"/>, at once or over the stage's transition time.</summary>
    public static void Background(Stage stage, string? name, StageTransition? how)
    {
        name = StageLines.Names(stage.Backgrounds).FirstOrDefault(n => StageLines.Same(n, name)) ?? name?.Trim();
        if (StageLines.Same(name, stage.Background))
            return;
        var transition = how ?? stage.BackgroundTransition;
        if (transition == StageTransition.Cut || !(stage.TransitionTime > 0) || string.IsNullOrEmpty(stage.Background))
            (stage.LeavingBackground, stage.ChangeLeft) = (null, 0);
        else
            (stage.LeavingBackground, stage.Changing, stage.ChangeLeft) = (stage.Background, transition, stage.TransitionTime);
        stage.Background = name;
    }

    private static void Show(Stage stage, Character character, string? position, string? expression, StageTransition? how)
    {
        if (expression is { Length: > 0 })
            character.Expression = expression;
        if (character.IsOnStage)
        {
            if (position is { Length: > 0 } && !StageLines.Same(position, character.Position))
                Move(stage, character, position, how);
            return;
        }

        var spotName = position is { Length: > 0 } ? position : character.Position is { Length: > 0 } last ? last : MiddleOf(stage);
        var spot = stage.PositionOf(spotName) ?? Vector2.Zero;
        var transition = how ?? stage.CharacterTransition;
        var time = transition == StageTransition.Cut ? 0 : Math.Max(0, stage.TransitionTime);
        character.Entity!.IsEnabled = true;
        character.Leaving = false;
        SetPosition(character, spotName);
        character.Brightness = Lit(stage, character);
        if (transition == StageTransition.Slide && time > 0)
        {
            Place(character, stage, spot + Outward(stage, spot));
            StartMove(character, spot + Outward(stage, spot), spot, time);
        }
        else
        {
            character.MoveLeft = 0;
            Place(character, stage, spot);
        }

        (character.Shown, character.FadeTime) = time > 0 ? (0, time) : (1, 0);
    }

    private static void Hide(Stage stage, Character character, StageTransition? how)
    {
        if (!character.IsOnStage)
            return;
        var transition = how ?? stage.CharacterTransition;
        var time = transition == StageTransition.Cut ? 0 : Math.Max(0, stage.TransitionTime);
        if (time <= 0)
        {
            Gone(character);
            return;
        }

        character.Leaving = true;
        character.FadeTime = time;
        if (transition == StageTransition.Slide)
        {
            var at = Here(character, stage);
            StartMove(character, at, at + Outward(stage, at), time);
        }
    }

    private static void Move(Stage stage, Character character, string position, StageTransition? how)
    {
        if (stage.PositionOf(position) is not { } spot)
            return;
        SetPosition(character, position);
        var time = how == StageTransition.Cut ? 0 : Math.Max(0, stage.MoveTime);
        if (time <= 0 || !character.IsOnStage)
        {
            character.MoveLeft = 0;
            Place(character, stage, spot);
        }
        else
        {
            StartMove(character, Here(character, stage), spot, time);
        }
    }

    // The position's name without placing them there (a move does that over time).
    private static void SetPosition(Character character, string? position)
    {
        var moving = character.MoveLeft;
        character.MoveLeft = 1;
        character.Position = position;
        character.MoveLeft = moving;
    }

    private static void StartMove(Character character, Vector2 from, Vector2 to, float time) =>
        (character.MoveFrom, character.MoveTo, character.MoveLeft, character.MoveTime) = (from, to, time, time);

    // Off stage for good: hidden, whole again for next time.
    private static void Gone(Character character)
    {
        character.Entity!.IsEnabled = false;
        (character.Leaving, character.Shown, character.FadeTime, character.MoveLeft) = (false, 1, 0, 0);
        if (Stage.StageOf(character.Entity) is { } stage && stage.PositionOf(character.Position) is { } spot)
            Place(character, stage, spot);
    }

    // The middle position (the one nearest the middle), for someone shown without one.
    private static string? MiddleOf(Stage stage) =>
        StageLines.Spots(stage.Positions).OrderBy(s => s.At.LengthSquared()).Select(s => s.Name).FirstOrDefault();

    // Away from the middle, toward the side the spot is on (right of the middle goes right).
    private static Vector2 Outward(Stage stage, Vector2 spot) => new(spot.X < 0 ? -stage.SlideDistance : stage.SlideDistance, 0);

    // How lit someone is while nobody, or they, are speaking.
    private static float Lit(Stage stage, Character character) =>
        stage.Speaking.Length == 0 || StageLines.Same(stage.Speaking, character.Name) ? 1 : 1 - Math.Clamp(stage.DimOthers, 0, 1);

    /// <summary>Where <paramref name="character"/> is from <paramref name="stage"/>'s middle.</summary>
    public static Vector2 Here(Character character, Stage stage)
    {
        var world = character.Entity!.Transform.WorldPosition;
        var middle = stage.Entity!.Transform.WorldPosition;
        return new Vector2(world.X - middle.X, world.Y - middle.Y);
    }

    /// <summary>Puts <paramref name="character"/> at <paramref name="at"/> from <paramref name="stage"/>'s middle, keeping their depth.</summary>
    public static void Place(Character character, Stage stage, Vector2 at)
    {
        var entity = character.Entity!;
        var middle = stage.Entity!.Transform.WorldPosition;
        var world = new Vector3(middle.X + at.X, middle.Y + at.Y, entity.Transform.WorldPosition.Z);
        if (entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
            world = Vector3.Transform(world, inverse);
        entity.Transform.Position = new Vector3(world.X, world.Y, entity.Transform.Position.Z);
    }

    /// <summary>
    /// Plays the stage on by <paramref name="seconds"/>: the background's change, and each character's fade, walk or slide
    /// and lighting up or dimming as <paramref name="speaking"/> (the character speaking, or null) says.
    /// </summary>
    public static void Step(Stage stage, IReadOnlyList<Character> cast, Character? speaking, float seconds)
    {
        if (stage.ChangeLeft > 0)
        {
            stage.ChangeLeft = Math.Max(0, stage.ChangeLeft - seconds);
            if (stage.ChangeLeft <= 0)
                stage.LeavingBackground = null;
        }

        stage.Speaking = speaking?.Name ?? "";
        var on = 0;
        foreach (var character in cast)
        {
            if (character.Entity is not { IsEnabled: true })
                continue;
            if (!character.Leaving)
                on++;

            if (character.MoveLeft > 0)
            {
                character.MoveLeft = Math.Max(0, character.MoveLeft - seconds);
                var t = character.MoveTime > 0 ? 1 - character.MoveLeft / character.MoveTime : 1;
                var eased = t * t * (3 - 2 * t);
                Place(character, stage, Vector2.Lerp(character.MoveFrom, character.MoveTo, eased));
            }

            if (character.FadeTime > 0)
            {
                var step = seconds / character.FadeTime;
                character.Shown = Math.Clamp(character.Shown + (character.Leaving ? -step : step), 0, 1);
                if (!character.Leaving && character.Shown >= 1)
                    character.FadeTime = 0;
            }

            if (character.Leaving && (character.FadeTime <= 0 || character.Shown <= 0) && character.MoveLeft <= 0)
            {
                Gone(character);
                continue;
            }

            var lit = speaking is null || speaking == character ? 1 : 1 - Math.Clamp(stage.DimOthers, 0, 1);
            var change = seconds / DimTime;
            character.Brightness = character.Brightness < lit
                ? Math.Min(lit, character.Brightness + change)
                : Math.Max(lit, character.Brightness - change);
        }

        stage.OnStage = on;
    }

    /// <summary>Draws the stage's background, and the one it's changing from while a fade or slide is under way.</summary>
    public static void DrawBackground(Stage stage, Drawing drawing)
    {
        var looks = StageLines.Looks(stage.Backgrounds);
        var now = looks.FirstOrDefault(l => StageLines.Same(l.Name, stage.Background));
        var size = new Vector2(Math.Abs(stage.Size.X), Math.Abs(stage.Size.Y));
        if (size.X <= 0 || size.Y <= 0)
            return;
        var world = stage.Entity!.Transform.WorldMatrix;
        var changing = drawing.IsPlaying && stage.ChangeLeft > 0 && stage.TransitionTime > 0 &&
                       looks.FirstOrDefault(l => StageLines.Same(l.Name, stage.LeavingBackground)) is { Name: not null } before
            ? before
            : (StageLines.Look?)null;
        var t = changing is null ? 1 : Math.Clamp(1 - stage.ChangeLeft / stage.TransitionTime, 0, 1);
        var eased = t * t * (3 - 2 * t);
        if (changing is { } old)
        {
            var offset = stage.Changing == StageTransition.Slide ? new Vector2(-size.X * eased, 0) : Vector2.Zero;
            Draw(drawing, stage, world, old, size, offset, 1);
        }

        if (now.Name is null)
            return;
        if (changing is null)
            Draw(drawing, stage, world, now, size, Vector2.Zero, 1);
        else if (stage.Changing == StageTransition.Slide)
            Draw(drawing, stage, world, now, size, new Vector2(size.X * (1 - eased), 0), 1);
        else
            Draw(drawing, stage, world, now, size, Vector2.Zero, eased);
    }

    private static void Draw(Drawing drawing, Stage stage, Matrix4x4 world, StageLines.Look look, Vector2 size, Vector2 offset, float alpha)
    {
        if (look.Image is { Length: > 0 } image)
        {
            var tint = drawing.Tint(stage, Color.White);
            drawing.Image(Matrix4x4.CreateScale(size.X, size.Y, 1) * Matrix4x4.CreateTranslation(offset.X, offset.Y, 0) * world, image,
                tint with { A = tint.A * alpha });
            return;
        }

        var band = size.Y / Bands;
        for (var i = 0; i < Bands; i++)
        {
            var color = drawing.Tint(stage, Color.Lerp(look.Top, look.Bottom, Bands == 1 ? 0 : i / (Bands - 1f)));
            var middle = new Vector2(offset.X, offset.Y + size.Y / 2 - band * (i + 0.5f));
            // A hair taller than a band, so no seam shows between them.
            drawing.Add(Quads.Rect(stage.Entity!, middle, new Vector2(size.X, band * 1.04f), color with { A = color.A * alpha }, world));
        }
    }
}
