using System.Globalization;
using System.Numerics;
using System.Text;
using Griff.Data;

namespace Griff.Modules.Fighting;

/// <summary>Where a move's hits must be blocked: high or low (mid), standing (high, an overhead) or crouching (low).</summary>
public enum MoveHeight
{
    /// <summary>Blocked standing or crouching.</summary>
    Mid,

    /// <summary>Blocked only crouching: a sweep.</summary>
    Low,

    /// <summary>Blocked only standing: an overhead.</summary>
    High,
}

/// <summary>
/// One of a <see cref="FightingCharacter"/>'s moves: its name, the input that does it (<see cref="MoveInput"/>), the animation clip it
/// plays (whose hitboxes, drawn in the Clip Editor, are what hit), how many steps it takes before it hits, hits for and
/// takes to recover (its startup, active and recovery frames), where it must be blocked, and what it cancels into. Written
/// as a line, e.g. <c>Jab: 5P frames 3/2/6 cancels special</c> or <c>Uppercut: 623P clip Rising frames 3/6/22 push 1,7</c>.
/// </summary>
/// <param name="Name">What it's called, e.g. "Jab" or "Fireball".</param>
/// <param name="Input">What's pressed for it, e.g. 236P.</param>
/// <param name="Clip">The animation clip it plays; empty for one named like the move without spaces ("Low Kick" plays LowKick).</param>
/// <param name="Startup">Steps (frames, at 60 a second) before it can hit; 0 for as long as its clip's frames before its first hitbox take.</param>
/// <param name="Active">Steps it can hit for; 0 for as long as its clip's frames with hitboxes take.</param>
/// <param name="Recovery">Steps after it hits before the fighter can act again; 0 for as long as its clip's last frames take.</param>
/// <param name="Hits">Where it must be blocked: mid (either way), low (crouching) or high (standing).</param>
/// <param name="Blockstun">Steps a blocked hit holds the blocker in blockstun; 0 for two thirds of its hitbox's stun.</param>
/// <param name="Chip">How much of its damage a blocked hit still does, 0 to 1; null for the blocker's Chip Damage.</param>
/// <param name="Push">How fast it sends the fighter, forward and up, as it starts to hit (a lunge, a rising uppercut); zero for none.</param>
/// <param name="Cancels">
/// What it cancels into once it has hit or been blocked: moves by name, "special" (any special move), "normal" (any
/// other) or "any"; empty for nothing.
/// </param>
/// <param name="Message">A message sent with <c>Game.Send</c> as it starts, with the fighter as its data.</param>
/// <param name="Sound">A sound played as it starts, e.g. "Sounds/swing.wav".</param>
public sealed record FighterMove(
    string Name,
    MoveInput Input,
    string? Clip = null,
    int Startup = 0,
    int Active = 0,
    int Recovery = 0,
    MoveHeight Hits = MoveHeight.Mid,
    int Blockstun = 0,
    float? Chip = null,
    Vector2 Push = default,
    IReadOnlyList<string>? Cancels = null,
    string? Message = null,
    string? Sound = null)
{
    /// <summary>The clip it plays: its own, or its name without spaces.</summary>
    public string ClipName => string.IsNullOrWhiteSpace(Clip) ? Name.Replace(" ", "") : Clip.Trim();

    /// <summary>What it cancels into, never null.</summary>
    public IReadOnlyList<string> CancelList => Cancels ?? [];

    /// <summary>True if, once it has hit or been blocked, it can be cancelled into <paramref name="next"/>.</summary>
    public bool CancelsInto(FighterMove next)
    {
        foreach (var cancel in CancelList)
        {
            if (cancel.Equals("any", StringComparison.OrdinalIgnoreCase) ||
                cancel.Equals("special", StringComparison.OrdinalIgnoreCase) && next.Input.IsSpecial ||
                cancel.Equals("normal", StringComparison.OrdinalIgnoreCase) && !next.Input.IsSpecial ||
                cancel.Equals(next.Name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reads a move written as a line: its name and a colon, its input, then any of <c>clip Name</c>, <c>frames 3/2/6</c>
    /// (or <c>startup</c>, <c>active</c> and <c>recovery</c> on their own), <c>low</c> or <c>high</c>, <c>blockstun 10</c>,
    /// <c>chip 0.25</c>, <c>push 2,0</c>, <c>message Name</c>, <c>sound Path</c> and last <c>cancels special</c> (or names, with
    /// commas between, to the end of the line). False with why for a line that doesn't make sense.
    /// </summary>
    public static bool TryParse(string? line, out FighterMove move, out string? problem)
    {
        move = null!;
        problem = null;
        var text = (line ?? "").Trim();
        var colon = text.IndexOf(':');
        if (colon <= 0)
        {
            problem = "it needs a name and a colon first, e.g. Jab: 5P";
            return false;
        }

        var name = text[..colon].Trim();
        var words = text[(colon + 1)..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            problem = "it has no input after the colon (e.g. 5P or 236P)";
            return false;
        }

        if (!MoveInput.TryParse(words[0], out var input, out var inputProblem))
        {
            problem = inputProblem;
            return false;
        }

        move = new FighterMove(name, input);
        for (var i = 1; i < words.Length; i++)
        {
            var word = words[i].ToLowerInvariant();
            string? Next() => i + 1 < words.Length ? words[++i] : null;
            switch (word)
            {
                case "low":
                    move = move with { Hits = MoveHeight.Low };
                    break;
                case "high":
                    move = move with { Hits = MoveHeight.High };
                    break;
                case "mid":
                    move = move with { Hits = MoveHeight.Mid };
                    break;
                case "clip":
                    move = move with { Clip = Next() };
                    break;
                case "frames":
                    var parts = (Next() ?? "").Split('/');
                    if (parts.Length != 3 || !Steps(parts[0], out var s) || !Steps(parts[1], out var a) || !Steps(parts[2], out var r))
                    {
                        problem = "frames are startup, active and recovery, e.g. frames 3/2/6";
                        return false;
                    }

                    move = move with { Startup = s, Active = a, Recovery = r };
                    break;
                case "startup" or "active" or "recovery" or "blockstun":
                    if (!Steps(Next(), out var steps))
                    {
                        problem = $"{word} is a whole number of steps, e.g. {word} 4";
                        return false;
                    }

                    move = word switch
                    {
                        "startup" => move with { Startup = steps },
                        "active" => move with { Active = steps },
                        "recovery" => move with { Recovery = steps },
                        _ => move with { Blockstun = steps },
                    };
                    break;
                case "chip":
                    if (!float.TryParse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture, out var chip) || chip is < 0 or > 1)
                    {
                        problem = "chip is how much of a blocked hit's damage it still does, from 0 to 1, e.g. chip 0.25";
                        return false;
                    }

                    move = move with { Chip = chip };
                    break;
                case "push":
                    if (!TryVector(Next(), out var push))
                    {
                        problem = "push is forward and up speeds, e.g. push 2,0";
                        return false;
                    }

                    move = move with { Push = push };
                    break;
                case "message":
                    move = move with { Message = Next() };
                    break;
                case "sound":
                    move = move with { Sound = Next() };
                    break;
                case "cancels":
                    // Names, with commas between, to the end of the line (so a move called "Low Kick" can be named).
                    var rest = string.Join(' ', words[(i + 1)..]);
                    i = words.Length;
                    move = move with
                    {
                        Cancels = [.. rest.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Where(c => !c.Equals("none", StringComparison.OrdinalIgnoreCase))],
                    };
                    break;
                default:
                    problem = $"\"{words[i]}\" isn't a setting a move has (clip, frames, low, high, blockstun, chip, push, cancels, message, sound)";
                    return false;
            }
        }

        return true;
    }

    /// <summary>Reads a move's line, throwing for one that doesn't make sense.</summary>
    public static FighterMove Parse(string line) =>
        TryParse(line, out var move, out var problem) ? move : throw new FormatException($"\"{line}\": {problem}.");

    /// <summary>The moves of <paramref name="lines"/> that make sense, in order.</summary>
    public static IReadOnlyList<FighterMove> ParseAll(IEnumerable<string>? lines) =>
        [.. (lines ?? []).Select(l => TryParse(l, out var move, out _) ? move : null).OfType<FighterMove>()];

    /// <summary>The move as a line, with what isn't the default: what <see cref="TryParse"/> reads back.</summary>
    public override string ToString()
    {
        var text = new StringBuilder($"{Name}: {Input}");
        if (!string.IsNullOrWhiteSpace(Clip) && Clip.Trim() != Name.Replace(" ", ""))
            text.Append(" clip ").Append(Clip.Trim());
        if (Startup != 0 || Active != 0 || Recovery != 0)
            text.Append(CultureInfo.InvariantCulture, $" frames {Startup}/{Active}/{Recovery}");
        if (Hits != MoveHeight.Mid)
            text.Append(Hits == MoveHeight.Low ? " low" : " high");
        if (Blockstun != 0)
            text.Append(CultureInfo.InvariantCulture, $" blockstun {Blockstun}");
        if (Chip is { } chip)
            text.Append(" chip ").Append(Number(chip));
        if (Push != Vector2.Zero)
            text.Append(" push ").Append(Number(Push.X)).Append(',').Append(Number(Push.Y));
        if (!string.IsNullOrWhiteSpace(Message))
            text.Append(" message ").Append(Message.Trim());
        if (!string.IsNullOrWhiteSpace(Sound))
            text.Append(" sound ").Append(Sound.Trim());
        if (CancelList.Count > 0)
            text.Append(" cancels ").Append(string.Join(", ", CancelList));
        return text.ToString();
    }

    /// <summary>
    /// A move from a row of a moves table: the row's name, and its columns Input, Clip, Startup, Active, Recovery (or Frames,
    /// "3/2/6"), Hits (Low, High or Mid), Blockstun, Chip, Push ("2,0"), Cancels, Message and Sound, any of them left out.
    /// False with why for a row that doesn't make a move.
    /// </summary>
    public static bool TryFromRow(TableRow row, out FighterMove move, out string? problem)
    {
        var line = new StringBuilder(row.Name.Replace(":", "")).Append(": ").Append(Cell("Input")?.Replace(" ", ""));
        void Add(string word, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                line.Append(' ').Append(word).Append(' ').Append(value.Trim());
        }

        Add("clip", Cell("Clip"));
        if (Cell("Frames") is { Length: > 0 } frames)
            Add("frames", frames.Replace(" ", ""));
        foreach (var steps in new[] { "Startup", "Active", "Recovery", "Blockstun" })
        {
            if (Cell(steps) is { Length: > 0 } value && value != "0")
                Add(steps.ToLowerInvariant(), value);
        }

        if (Cell("Hits") is { Length: > 0 } hits && !hits.Equals("mid", StringComparison.OrdinalIgnoreCase))
            line.Append(' ').Append(hits.Trim().ToLowerInvariant());
        Add("chip", Cell("Chip"));
        Add("push", Cell("Push")?.Replace(" ", ""));
        Add("message", Cell("Message"));
        Add("sound", Cell("Sound"));
        Add("cancels", Cell("Cancels"));
        return TryParse(line.ToString(), out move, out problem);

        string? Cell(string column) => row.Table.ColumnIndex(column) is >= 0 and var i && !row.Cells[i].IsEmpty
            ? row.Cells[i].ToString()
            : null;
    }

    private static bool Steps(string? text, out int steps) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out steps) && steps is >= 0 and <= 999;

    private static bool TryVector(string? text, out Vector2 value)
    {
        value = default;
        var parts = (text ?? "").Split(',');
        if (parts.Length != 2 ||
            !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !float.IsFinite(x) || !float.IsFinite(y))
            return false;
        value = new Vector2(x, y);
        return true;
    }

    private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
