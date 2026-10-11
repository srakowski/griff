using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Agent;

/// <summary>Playing the game through an agent: the keys it names, and what's reported besides where things are.</summary>
public static class Playtesting
{
    /// <summary>
    /// The key called <paramref name="name"/>, in any case: a <see cref="Key"/>'s name like "Space" or "LeftShift", or a
    /// digit for its number key ("1" is <see cref="Key.D1"/>). Null for anything else.
    /// </summary>
    public static Key? KeyNamed(string name)
    {
        name = name.Trim();
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
            return Key.D0 + (name[0] - '0');
        // Enum.TryParse takes numbers as values too ("3" would be the C key), so only names count.
        return name.Length > 0 && !char.IsAsciiDigit(name[0]) && Enum.TryParse<Key>(name, ignoreCase: true, out var key) ? key : null;
    }

    /// <summary>
    /// The dialogue being said, if any: whose it is, the line showing and the choices offered, the picked one marked, e.g.
    /// <c>Talking: Shopkeeper says "Want a sword?". Choices: 1 "Yes" (picked), 2 "No"; ...</c>
    /// </summary>
    public static string? Talking(Game game)
    {
        if (game.Talking is not { } dialogue)
            return null;
        var (kind, said, _) = dialogue.Row(dialogue.Line);
        var text = $"Talking: {dialogue.Entity?.Name}" + (kind == Dialogue.RowKind.Choice ? "." : $" says \"{said}\".");
        if (dialogue.Choices.Count == 0)
            return text;
        var choices = dialogue.Choices.Select((choice, i) => $"{i + 1} \"{choice}\"{(i == dialogue.Picked ? " (picked)" : "")}");
        return $"{text} Choices: {string.Join(", ", choices)}; tap a number, or Up or Down and Interact, to pick.";
    }
}
