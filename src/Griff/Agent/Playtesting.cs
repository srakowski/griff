using System.Numerics;
using System.Text.Json;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Agent;

/// <summary>Playing the game through an agent: the keys and points it names, and what's reported besides where things are.</summary>
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
    /// The point in the world an agent names for the mouse: <c>[x, y]</c> in world units, or an entity's name for its
    /// middle (its collider's, if it has one). Throws an <see cref="AgentException"/> saying what's wrong otherwise.
    /// </summary>
    public static Vector2 PointOf(JsonElement value, Scene scene)
    {
        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 2 &&
            value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number))
            return new Vector2(value[0].GetSingle(), value[1].GetSingle());
        if (value.ValueKind == JsonValueKind.String)
        {
            var name = value.GetString()!;
            var entity = scene.FindNamed(name, activeOnly: true) ?? scene.FindNamed(name)
                         ?? throw new AgentException($"There's no \"{name}\" in the game to point at.");
            return Game.CenterOf(entity);
        }

        throw new AgentException($"A point is [x, y] in world units or an entity's name, not {value.GetRawText()}.");
    }

    /// <summary>
    /// What <paramref name="entity"/>'s drag and drop is doing, to add to where it is: <c>, held</c>, <c>, on Slot 2</c>, or
    /// for a drop target <c>, holding Card, Gem (full)</c>. Empty for anything else.
    /// </summary>
    public static string Dragging(Entity entity)
    {
        var text = "";
        if (entity.GetComponent<Draggable>() is { } thing)
            text += thing.IsHeld ? ", held" : thing.Target is { } target ? $", on {target.Name}" : "";
        if (entity.GetComponent<DropTarget>() is { } slot)
            text += slot.Contents.Count == 0 ? ", empty" : $", holding {string.Join(", ", slot.Contents.Select(e => e.Name))}{(slot.IsFull ? " (full)" : "")}";
        return text;
    }

    /// <summary>Where the mouse is and what it's holding, e.g. <c>Mouse: at (2.5, 1), holding Card.</c></summary>
    public static string Mouse(Game game)
    {
        var at = game.Input.MouseWorld;
        return $"Mouse: at ({at.X:0.##}, {at.Y:0.##}){(game.Held is { } held ? $", holding {held.Name}" : "")}.";
    }

    /// <summary>
    /// The dialogue being said, if any: whose it is (with the speaker saying the line in brackets, when that's someone
    /// else), the line showing and the choices offered, the picked one marked, e.g.
    /// <c>Talking: Shopkeeper says "Want a sword?". Choices: 1 "Yes" (picked), 2 "No"; ...</c>
    /// </summary>
    public static string? Talking(Game game)
    {
        if (game.Talking is not { } dialogue)
            return null;
        var who = dialogue.Speaker is { } speaker && speaker.ShownName != dialogue.Entity?.Name
            ? $"{dialogue.Entity?.Name} ({speaker.ShownName})"
            : dialogue.Entity?.Name;
        var text = $"Talking: {who}" + (dialogue.Said is { } said ? $" says \"{said}\"." : ".");
        if (game.IsTyping(dialogue))
            text += " It's still typing out (Interact shows it all at once; choices wait for it).";
        if (dialogue.Choices.Count == 0)
            return text;
        var choices = dialogue.Choices.Select((choice, i) => $"{i + 1} \"{choice}\"{(i == dialogue.Picked ? " (picked)" : "")}");
        return $"{text} Choices: {string.Join(", ", choices)}; tap a number, or Up or Down and Interact, to pick.";
    }

    /// <summary>
    /// The flags set and cleared while playing (each name with whether it's set at the end), e.g.
    /// <c>Flags: set MetGuide; cleared HasKey.</c> Null when none changed.
    /// </summary>
    public static string? Flags(IReadOnlyDictionary<string, bool> changed)
    {
        if (changed.Count == 0)
            return null;
        var set = changed.Where(f => f.Value).Select(f => f.Key).ToList();
        var cleared = changed.Where(f => !f.Value).Select(f => f.Key).ToList();
        var parts = new List<string>();
        if (set.Count > 0)
            parts.Add("set " + string.Join(", ", set));
        if (cleared.Count > 0)
            parts.Add("cleared " + string.Join(", ", cleared));
        return $"Flags: {string.Join("; ", parts)}.";
    }

    /// <summary>
    /// The flags set in <paramref name="game"/>'s save data (every value saved as true), e.g. <c>Flags set: MetGuide,
    /// HasKey.</c> Null when none are.
    /// </summary>
    public static string? FlagsSet(Game game)
    {
        var set = game.SaveData.Keys.Where(key => game.SaveData.GetBool(key)).ToList();
        return set.Count == 0 ? null : $"Flags set: {string.Join(", ", set)}.";
    }
}
