using System.Globalization;
using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.VisualNovels;

/// <summary>How something comes onto a <see cref="Stage"/>, leaves it or changes on it.</summary>
public enum StageTransition
{
    /// <summary>At once.</summary>
    Cut,

    /// <summary>Fading in (or out, or one background into the next).</summary>
    Fade,

    /// <summary>
    /// Sliding: a character slides in from the side of the stage they stand on (and out that way), a new background slides
    /// in over the old one from the right.
    /// </summary>
    Slide,
}

/// <summary>What a direction to a <see cref="Stage"/> does: one of the dialogue tags "[show]", "[hide]", "[move]" and "[background]".</summary>
public enum DirectionKind
{
    /// <summary>Brings a character on (or changes where they stand and how they look, when they're on already).</summary>
    Show,

    /// <summary>Takes a character off, or everyone with "all".</summary>
    Hide,

    /// <summary>Moves a character to another position.</summary>
    Move,

    /// <summary>Changes the background.</summary>
    Background,
}

/// <summary>
/// One direction to a <see cref="Stage"/>, as a dialogue line's tag says it: "[show Ava left happy]" is Show, Who "Ava",
/// Position "Left" and Expression "Happy"; "[background Park fade]" is Background "Park" with a fade. Words it doesn't
/// know are kept in <see cref="Unknown"/>, so the game and the editor can say so.
/// </summary>
public sealed record StageDirection(DirectionKind Kind, string? Who = null, string? Position = null, string? Expression = null,
    string? Background = null, StageTransition? Transition = null)
{
    /// <summary>Words that are none of the stage's characters, positions, expressions, backgrounds or transitions.</summary>
    public IReadOnlyList<string> Unknown { get; init; } = [];

    /// <summary>The word a tag starts with for it: "show", "hide", "move" or "background".</summary>
    public string Word => Kind switch
    {
        DirectionKind.Show => "show",
        DirectionKind.Hide => "hide",
        DirectionKind.Move => "move",
        _ => "background",
    };

    /// <summary>True for "[hide all]": everyone leaves.</summary>
    public bool IsEveryone => Kind == DirectionKind.Hide && StageLines.IsEveryone(Who);

    /// <summary>The tag as it's written on a line, e.g. "[show Ava left happy fade]".</summary>
    public string ToTag()
    {
        var words = new List<string> { Word };
        if (Kind == DirectionKind.Background)
            words.Add(Background ?? "");
        else
            words.Add(Who ?? "");
        if (Position is { Length: > 0 })
            words.Add(Position);
        if (Expression is { Length: > 0 })
            words.Add(Expression);
        if (Transition is { } how)
            words.Add(how.ToString().ToLowerInvariant());
        return "[" + string.Join(' ', words.Where(w => w.Length > 0)) + "]";
    }

    /// <summary>What it does, in a few words: "Show Ava at Left, Happy (fade)".</summary>
    public string Describe()
    {
        var how = Transition is { } t ? $" ({t.ToString().ToLowerInvariant()})" : "";
        return Kind switch
        {
            DirectionKind.Background => $"Background {Background}{how}",
            DirectionKind.Hide when IsEveryone => $"Hide everyone{how}",
            DirectionKind.Hide => $"Hide {Who}{how}",
            DirectionKind.Move => $"Move {Who} to {Position}{how}",
            _ => $"Show {Who}{(Position is { Length: > 0 } p ? " at " + p : "")}{(Expression is { Length: > 0 } e ? ", " + e : "")}{how}",
        };
    }
}

/// <summary>
/// Reading what a visual novel's stage is told: its positions, backgrounds and a character's expressions (rows of "Name:
/// value"), the directions on dialogue lines, and the lines themselves taken apart and put back together, for the editor's
/// Dialogue Editor and for agents. And what the stage looks like at any line of a dialogue (<see cref="At"/>), read top to
/// bottom.
/// </summary>
public static class StageLines
{
    /// <summary>The words that start a stage's dialogue tags: "[show ...]", "[hide ...]", "[move ...]" and "[background ...]" (or "[bg ...]").</summary>
    public static IReadOnlyList<string> TagWords { get; } =
        ((DialogueTagsAttribute)Attribute.GetCustomAttribute(typeof(Stage), typeof(DialogueTagsAttribute))!).Words;

    /// <summary>How things come and go, as a tag says them: cut, fade and slide.</summary>
    public static IReadOnlyList<string> TransitionWords { get; } = ["cut", "fade", "slide"];

    // Little words a direction can have that say nothing: "[show Ava at left, with a smile]".
    private static readonly HashSet<string> Filler = new(["at", "to", "with", "and", "on", "the", "in", "as", "looking"], StringComparer.OrdinalIgnoreCase);

    /// <summary>True for the word that means everyone on stage: "all" or "everyone".</summary>
    public static bool IsEveryone(string? who) =>
        who is not null && (who.Equals("all", StringComparison.OrdinalIgnoreCase) || who.Equals("everyone", StringComparison.OrdinalIgnoreCase));

    // ----- "Name: value" rows -----

    /// <summary>A row "Name: value" taken apart, or null when it has no name.</summary>
    public static (string Name, string Value)? Entry(string? row)
    {
        if (string.IsNullOrWhiteSpace(row))
            return null;
        var colon = row.IndexOf(':');
        var name = (colon < 0 ? row : row[..colon]).Trim();
        return name.Length == 0 ? null : (name, colon < 0 ? "" : row[(colon + 1)..].Trim());
    }

    /// <summary>The names the rows give, in order.</summary>
    public static IReadOnlyList<string> Names(IReadOnlyList<string>? rows) =>
        rows is null ? [] : [.. rows.Select(Entry).OfType<(string Name, string Value)>().Select(e => e.Name)];

    /// <summary>A position on a stage: its name and where it is from the stage's middle.</summary>
    public readonly record struct Spot(string Name, Vector2 At);

    /// <summary>A stage's positions, from rows like "Left: -4.5" or "Far Left: -7, -0.5" (across, and up).</summary>
    public static IReadOnlyList<Spot> Spots(IReadOnlyList<string>? rows)
    {
        var spots = new List<Spot>();
        foreach (var entry in rows?.Select(Entry).OfType<(string Name, string Value)>() ?? [])
        {
            var numbers = entry.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(n => float.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : float.NaN).ToList();
            if (numbers.Count == 0 || numbers.Any(float.IsNaN))
                continue;
            spots.Add(new Spot(entry.Name, new Vector2(numbers[0], numbers.Count > 1 ? numbers[1] : 0)));
        }

        return spots;
    }

    /// <summary>
    /// A background: a picture (<see cref="Image"/>), or a color, or two colors running from the top to the bottom.
    /// </summary>
    public readonly record struct Look(string Name, string? Image, Color Top, Color Bottom);

    /// <summary>
    /// A stage's backgrounds, from rows like "Park: Backgrounds/park.png" (a picture), "Night: #10183A" (a color) or
    /// "Dusk: #F2A65A #3A2F5B" (a color at the top running to another at the bottom).
    /// </summary>
    public static IReadOnlyList<Look> Looks(IReadOnlyList<string>? rows)
    {
        var looks = new List<Look>();
        foreach (var (name, value) in rows?.Select(Entry).OfType<(string Name, string Value)>() ?? [])
        {
            var words = value.Split((char[])[' ', ','], StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is 1 or 2 && words[0].StartsWith('#') && Color.TryParse(words[0], out var top))
                looks.Add(new Look(name, null, top, words.Length == 2 && Color.TryParse(words[1], out var bottom) ? bottom : top));
            else if (value.Length > 0)
                looks.Add(new Look(name, value, Color.White, Color.White));
        }

        return looks;
    }

    /// <summary>
    /// A character's expression: its name and either a picture for their sprite ("Textures/ava-happy.png") or the name of a
    /// clip their animation plays ("Laugh").
    /// </summary>
    public readonly record struct Face(string Name, string Value)
    {
        /// <summary>True when it's a picture rather than a clip.</summary>
        public bool IsImage => Value.Contains('/') || Value.Contains('\\') || Path.HasExtension(Value);
    }

    /// <summary>A character's expressions, from rows like "Happy: Textures/ava-happy.png" or "Laugh: Laugh" (a clip).</summary>
    public static IReadOnlyList<Face> Faces(IReadOnlyList<string>? rows) =>
        rows is null ? [] : [.. rows.Select(Entry).OfType<(string Name, string Value)>().Where(e => e.Value.Length > 0).Select(e => new Face(e.Name, e.Value))];

    // ----- Directions -----

    /// <summary>The kind of direction a tag starting with <paramref name="word"/> gives, or null for a word that isn't one.</summary>
    public static DirectionKind? KindOf(string word) => word.ToLowerInvariant() switch
    {
        "show" => DirectionKind.Show,
        "hide" => DirectionKind.Hide,
        "move" => DirectionKind.Move,
        "background" or "bg" => DirectionKind.Background,
        _ => null,
    };

    /// <summary>
    /// Reads a direction: the tag's <paramref name="word"/> ("show") and the rest (<paramref name="args"/>, "Ava left
    /// happy fade"), against <paramref name="stage"/>'s characters, positions, backgrounds and expressions. Words can come
    /// in any order after the character's name, with commas and little words between ("at left, happy"). Null for a word
    /// that isn't a direction.
    /// </summary>
    public static StageDirection? Read(string word, string args, Stage? stage)
    {
        if (KindOf(word) is not { } kind)
            return null;
        var words = args.Split((char[])[' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries).Where(w => !Filler.Contains(w)).ToList();
        StageTransition? how = null;
        foreach (var w in words.ToList())
        {
            if (Enum.TryParse<StageTransition>(w, ignoreCase: true, out var t) && TransitionWords.Contains(w.ToLowerInvariant()))
            {
                how = t;
                words.Remove(w);
            }
        }

        if (kind == DirectionKind.Background)
        {
            var asked = string.Join(' ', words);
            var known = stage is null ? [] : Names(stage.Backgrounds);
            var name = known.FirstOrDefault(n => Same(n, asked));
            return new StageDirection(kind, Background: name ?? asked, Transition: how)
            {
                Unknown = name is null && stage is not null && asked.Length > 0 ? [asked] : [],
            };
        }

        // The character's name is the longest run of words that names one (names can have spaces: "Old Man").
        var cast = stage is null ? [] : stage.Cast.Select(c => c.Name).ToList();
        string? who = null;
        var taken = 0;
        for (var count = words.Count; count >= 1 && who is null; count--)
        {
            var name = string.Join(' ', words.Take(count));
            if (cast.FirstOrDefault(c => Same(c, name)) is { } found)
                (who, taken) = (found, count);
            else if (count == 1 && IsEveryone(name))
                (who, taken) = (name.ToLowerInvariant(), 1);
        }

        var unknown = new List<string>();
        if (who is null && words.Count > 0)
        {
            (who, taken) = (words[0], 1);
            if (stage is not null)
                unknown.Add(words[0]);
        }

        var character = stage?.Cast.FirstOrDefault(c => Same(c.Name, who));
        // With no stage to ask, a stage's usual positions are positions and anything else an expression.
        var spots = stage is null ? Names(Stage.DefaultPositions) : Names(stage.Positions);
        var faces = character is null ? [] : Names(character.Expressions);
        string? position = null, expression = null;
        foreach (var w in words.Skip(taken))
        {
            if (spots.FirstOrDefault(s => Same(s, w)) is { } spot)
                position = spot;
            else if (faces.FirstOrDefault(f => Same(f, w)) is { } face)
                expression = face;
            else if (stage is null || (character is null && kind == DirectionKind.Show))
                expression ??= w;
            else
                unknown.Add(w);
        }

        return new StageDirection(kind, who, position, expression, Transition: how) { Unknown = unknown };
    }

    // ----- Lines, taken apart and put back -----

    /// <summary>
    /// A row of a dialogue's lines taken apart for editing: what it starts with ("&gt; " for a choice), its tags in order
    /// (flags and directions, each as written, "[show Ava left]"), who says it and how they look ("Ava", "happy"), and
    /// what's said. <see cref="IsLine"/> is false for a row that isn't a line or a choice ("# Part", "-&gt; End", a
    /// row said as it is): only its tags and text are its own then.
    /// </summary>
    public sealed record LineParts(string Lead, IReadOnlyList<string> Tags, string? Speaker, string? Expression, string Text, bool IsLine)
    {
        /// <summary>The directions among its tags, read against <paramref name="stage"/> (null reads them as they are).</summary>
        public IReadOnlyList<StageDirection> Directions(Stage? stage) =>
            [.. Tags.Select(t => DirectionIn(t, stage)).OfType<StageDirection>()];
    }

    /// <summary>The direction a tag ("[show Ava left]") gives, or null for another kind of tag.</summary>
    public static StageDirection? DirectionIn(string tag, Stage? stage)
    {
        var inside = tag.Trim().TrimStart('[').TrimEnd(']').Trim();
        var space = inside.IndexOf(' ');
        return space < 0 ? null : Read(inside[..space], inside[(space + 1)..], stage);
    }

    /// <summary>Takes a row of a dialogue's lines apart (see <see cref="LineParts"/>).</summary>
    public static LineParts Split(string? row)
    {
        var text = (row ?? "").TrimStart();
        if (text.StartsWith('\\') || text.StartsWith('#') || text.StartsWith("->", StringComparison.Ordinal))
            return new LineParts("", [], null, null, row ?? "", IsLine: false);
        var lead = "";
        if (text.StartsWith('>'))
        {
            lead = "> ";
            text = text[1..].TrimStart();
        }

        var tags = new List<string>();
        while (text.StartsWith('[') && text.IndexOf(']') is var end and > 0 && IsTag(text[1..end]))
        {
            tags.Add(text[..(end + 1)]);
            text = text[(end + 1)..].TrimStart();
        }

        if (lead.Length == 0 && text.StartsWith("->", StringComparison.Ordinal))
            return new LineParts("", [], null, null, row ?? "", IsLine: false);
        if (lead.Length > 0 || text.StartsWith('\\'))
            return new LineParts(lead, tags, null, null, text, IsLine: lead.Length > 0);
        var (who, expression, said) = Dialogue.SplitSpeaker(text);
        return new LineParts("", tags, who, expression, who is null ? text : said, IsLine: true);
    }

    /// <summary>Puts a row back together from its parts (see <see cref="Split"/>).</summary>
    public static string Join(LineParts parts)
    {
        if (!parts.IsLine && parts.Lead.Length == 0 && parts.Tags.Count == 0)
            return parts.Text;
        var said = parts.Speaker is { Length: > 0 } who
            ? who + (parts.Expression is { Length: > 0 } e ? $" ({e})" : "") + ": " + parts.Text
            : parts.Text;
        return parts.Lead + string.Join(' ', parts.Tags.Append(said).Where(p => p.Length > 0));
    }

    // True for what's inside a tag's brackets: a flag's ("if Met", "set Met"...), or a module's (a direction).
    private static bool IsTag(string inside)
    {
        var words = inside.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 2)
            return false;
        return words[0].ToLowerInvariant() is "if" or "set" or "clear" or "send" || Dialogue.IsTagWord(words[0]);
    }

    // ----- The stage at a line -----

    /// <summary>One character as the stage shows them at a line: whether they're on, where, how they look, and whether they're speaking.</summary>
    public sealed record CastState(string Name, bool Shown, string? Position, string? Expression, bool Speaking);

    /// <summary>
    /// What a stage shows at a line: the background, everyone in its cast, who's speaking (null for nobody on stage: a
    /// narrator) and what's said, and the directions its words didn't make sense of.
    /// </summary>
    public sealed record StageState(string? Background, IReadOnlyList<CastState> Cast, string? Speaker, string Said, IReadOnlyList<string> Problems)
    {
        /// <summary>
        /// In a sentence: "Park. Ava at Left, Happy, speaking; Ben at Right, Sad, dimmed." Agents read it as the preview.
        /// </summary>
        public string Describe()
        {
            var on = Cast.Where(c => c.Shown).Select(c =>
                $"{c.Name}{(c.Position is { Length: > 0 } p ? " at " + p : "")}{(c.Expression is { Length: > 0 } e ? ", " + e : "")}" +
                (c.Speaking ? ", speaking" : Speaker is not null ? ", dimmed" : "")).ToList();
            var cast = on.Count == 0 ? "Nobody on stage" : string.Join("; ", on);
            var line = Said.Length == 0 ? "" : $" Says: {(Speaker is { } s && Cast.Any(c => c.Speaking) ? s + ": " : "")}{Said}";
            return $"{(Background is { Length: > 0 } b ? b : "No background")}. {cast}.{line}";
        }
    }

    /// <summary>
    /// What <paramref name="stage"/> shows at row <paramref name="row"/> of <paramref name="dialogue"/>'s lines, read top
    /// to bottom from the stage as it's set up: each row's directions done in turn (whatever its flags say, and whichever
    /// way the choices go), and the row's speaker on stage, with the expression they say it with.
    /// </summary>
    public static StageState At(Stage stage, Dialogue? dialogue, int row)
    {
        var cast = stage.Cast.Select(c => new Mutable(c.Name, c.Entity!.IsEnabled, c.Position, c.Expression)).ToList();
        var background = stage.Background;
        var problems = new List<string>();
        string? speaker = null;
        var said = "";
        var lines = dialogue?.Lines ?? [];
        for (var r = 0; r <= row && r < lines.Count; r++)
        {
            var parts = Split(lines[r]);
            foreach (var direction in parts.Directions(stage))
            {
                if (r == row)
                    problems.AddRange(direction.Unknown.Select(u => $"\"{u}\" in {direction.ToTag()} isn't a {Noun(direction.Kind)}"));
                switch (direction.Kind)
                {
                    case DirectionKind.Background:
                        background = direction.Background;
                        break;
                    case DirectionKind.Hide when direction.IsEveryone:
                        cast.ForEach(c => c.Shown = false);
                        break;
                    default:
                        if (cast.FirstOrDefault(c => Same(c.Name, direction.Who)) is not { } one)
                            break;
                        one.Shown = direction.Kind != DirectionKind.Hide;
                        one.Position = direction.Position ?? one.Position;
                        one.Expression = direction.Expression ?? one.Expression;
                        break;
                }
            }

            if (parts is { IsLine: true, Lead.Length: 0 } && parts.Text.Length > 0)
            {
                var talker = cast.FirstOrDefault(c => Same(c.Name, parts.Speaker));
                if (talker is not null && parts.Expression is { Length: > 0 } face)
                    talker.Expression = stage.Cast.FirstOrDefault(c => Same(c.Name, talker.Name)) is { } who && Names(who.Expressions).FirstOrDefault(n => Same(n, face)) is { } known ? known : face;
                if (r == row)
                {
                    speaker = talker?.Name ?? parts.Speaker;
                    said = parts.Text;
                }
            }
        }

        var speaking = cast.FirstOrDefault(c => c.Shown && Same(c.Name, speaker));
        return new StageState(background, [.. cast.Select(c => new CastState(c.Name, c.Shown, c.Position, c.Expression, c == speaking))],
            speaking?.Name, said, problems);
    }

    private static string Noun(DirectionKind kind) => kind == DirectionKind.Background ? "background of the stage's" : "character, position or expression it knows";

    private sealed class Mutable(string name, bool shown, string? position, string? expression)
    {
        public string Name { get; } = name;

        public bool Shown { get; set; } = shown;

        public string? Position { get; set; } = position;

        public string? Expression { get; set; } = expression;
    }

    internal static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
