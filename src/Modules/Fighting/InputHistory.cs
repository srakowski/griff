using System.Globalization;
using System.Text;

namespace Griff.Modules.Fighting;

/// <summary>
/// The last couple of seconds of a fighter's input, a step at a time: the direction held (a numpad digit, as the fighter
/// faces) and the buttons pressed that step (a bit each, in the order of its Buttons). Moves are read from it: a motion
/// from its directions, and a press made a few steps early from its presses, each taken once a move uses it. Kept with
/// the scene as text (<see cref="Write"/>), so a rewind into the middle of a motion finishes it.
/// </summary>
internal sealed class InputHistory
{
    /// <summary>How many steps it remembers: two seconds at 60 a step, enough for a charge and a motion after it.</summary>
    public const int Capacity = 128;

    private readonly byte[] _directions = new byte[Capacity];
    private readonly byte[] _presses = new byte[Capacity];
    private int _newest = -1;

    /// <summary>How many steps it holds, up to <see cref="Capacity"/>.</summary>
    public int Count { get; private set; }

    /// <summary>Changes each time it does, so its text is written again only when it has changed.</summary>
    public int Version { get; private set; }

    /// <summary>The direction held <paramref name="age"/> steps ago (0 is this step's); 5 before it began.</summary>
    public int Direction(int age) => age >= 0 && age < Count ? _directions[Index(age)] : 5;

    /// <summary>The buttons pressed <paramref name="age"/> steps ago and not yet used, a bit each.</summary>
    public int Presses(int age) => age >= 0 && age < Count ? _presses[Index(age)] : 0;

    /// <summary>Adds this step's direction and presses as the newest.</summary>
    public void Push(int direction, int presses)
    {
        _newest = (_newest + 1) % Capacity;
        _directions[_newest] = (byte)Math.Clamp(direction, 1, 9);
        _presses[_newest] = (byte)presses;
        Count = Math.Min(Count + 1, Capacity);
        Version++;
    }

    /// <summary>Takes button <paramref name="bit"/>'s press <paramref name="age"/> steps ago: a move came out of it, so it makes no other.</summary>
    public void Use(int age, int bit)
    {
        if (age < 0 || age >= Count)
            return;
        _presses[Index(age)] &= (byte)~(1 << bit);
        Version++;
    }

    public void Clear()
    {
        (_newest, Count) = (-1, 0);
        Array.Clear(_presses);
        Version++;
    }

    private int Index(int age) => (_newest - age + Capacity * 2) % Capacity;

    /// <summary>
    /// The history as text, oldest first: a direction a step, with the buttons pressed then, and a run of the same as
    /// "x*n", e.g. <c>5*40 2 3 6P</c>.
    /// </summary>
    public string Write(IReadOnlyList<string> buttons)
    {
        var text = new StringBuilder();
        var age = Count - 1;
        while (age >= 0)
        {
            var (direction, presses) = (Direction(age), Presses(age));
            var run = 1;
            while (age - run >= 0 && Direction(age - run) == direction && Presses(age - run) == presses)
                run++;
            if (text.Length > 0)
                text.Append(' ');
            text.Append((char)('0' + direction));
            var first = true;
            for (var bit = 0; bit < buttons.Count && bit < 8; bit++)
            {
                if ((presses & (1 << bit)) == 0)
                    continue;
                if (!first)
                    text.Append('+');
                text.Append(buttons[bit]);
                first = false;
            }

            if (run > 1)
                text.Append('*').Append(run.ToString(CultureInfo.InvariantCulture));
            age -= run;
        }

        return text.ToString();
    }

    /// <summary>Reads text written by <see cref="Write"/> back, its buttons by name (one no longer among <paramref name="buttons"/> is dropped).</summary>
    public void Read(string? text, IReadOnlyList<string> buttons)
    {
        Clear();
        foreach (var token in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token[0] is < '1' or > '9')
                continue;
            var star = token.IndexOf('*');
            var run = star > 0 && int.TryParse(token.AsSpan(star + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 1, Capacity) : 1;
            var names = star > 0 ? token[1..star] : token[1..];
            var presses = 0;
            foreach (var name in names.Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                for (var bit = 0; bit < buttons.Count && bit < 8; bit++)
                {
                    if (string.Equals(buttons[bit], name, StringComparison.OrdinalIgnoreCase))
                        presses |= 1 << bit;
                }
            }

            for (var i = 0; i < run; i++)
                Push(token[0] - '0', presses);
        }
    }
}
