using System.Globalization;
using System.Reflection;
using Griff.Editing;

namespace Griff.Scenes;

/// <summary>What kind of file in Assets an <see cref="AssetFileAttribute"/> property names.</summary>
public enum AssetFileKind
{
    /// <summary>A picture: a .png.</summary>
    Image,

    /// <summary>A sound effect: a .wav, .mp3 or .ogg.</summary>
    Sound,

    /// <summary>Music: a .wav, .mp3 or .ogg played as a song.</summary>
    Music,
}

/// <summary>
/// Marks a string property as a file in Assets that its name doesn't say it is (a deck's Back is a picture, note lanes'
/// Song is music): the Inspector gives it the picker, preview and maker that image and sound fields get.
/// </summary>
/// <param name="kind">What kind of file it names.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AssetFileAttribute(AssetFileKind kind) : Attribute
{
    public AssetFileKind Kind { get; } = kind;
}

/// <summary>
/// Marks a string property as naming something in another scene, the one another property names (a hotspot's Arrive At,
/// in the scene its Goes To names): the Inspector lists that scene's hotspots and other entities to pick from.
/// </summary>
/// <param name="scene">The property naming the scene, e.g. "GoesTo".</param>
public sealed class InSceneAttribute(string scene) : FieldEditorAttribute
{
    /// <summary>The property of the component naming the scene.</summary>
    public string Scene { get; } = scene;

    public override string Describe() =>
        $"Names something in the scene {System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(Scene)} names (a hotspot there, or any entity): the Inspector lists that scene's to pick from, and flags a name it doesn't have.";

    /// <summary>The scene <paramref name="component"/> names, or null for none.</summary>
    public string? SceneOf(Component component) =>
        component.GetType().GetProperty(Scene)?.GetValue(component) is string { Length: > 0 } name && name.Trim().Length > 0 ? name.Trim() : null;
}

/// <summary>
/// Marks a string property as a number of a target's components to change, "Value" or "Health.Max" (a component's name and
/// its property's): the Inspector lists the number properties of what's in the scene to pick from.
/// </summary>
public sealed class StatAttribute : FieldEditorAttribute
{
    public override string Describe() =>
        "A number of its target's components, by its property's name (\"Value\": any component's) or a component's and its property's (\"Health.Max\"). The Inspector lists the numbers of the components in the scene.";

    /// <summary>
    /// The numbers that can be named, "Component.Property", of the component types <paramref name="types"/>: every public
    /// whole or decimal number they have to read and set.
    /// </summary>
    public static IReadOnlyList<string> NumbersOf(IEnumerable<Type> types) =>
        [.. types.Distinct().OrderBy(t => t.Name, StringComparer.Ordinal).SelectMany(t => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => (p.PropertyType == typeof(float) || p.PropertyType == typeof(int)) && p.CanRead && p.SetMethod?.IsPublic == true &&
                !p.IsDefined(typeof(DoNotSerializeAttribute), true))
            .Select(p => $"{t.Name}.{p.Name}"))];
}

/// <summary>
/// Marks a string property as several names picked from what another member of its component lists (an AI fighter's Use
/// Moves, from its fighter's moves), separated by commas: the Inspector ticks them from a list, and flags a name that isn't one.
/// </summary>
/// <param name="member">A property (or method taking nothing) of the component giving the names, e.g. "MoveNames".</param>
public sealed class ChoicesOfAttribute(string member) : FieldEditorAttribute
{
    /// <summary>The member of the component that lists the choices.</summary>
    public string Member { get; } = member;

    /// <summary>What the choices are, for component_describe, e.g. "its fighter's moves".</summary>
    public string? Noun { get; init; }

    /// <summary>What none ticked means, e.g. "every one".</summary>
    public string? None { get; init; }

    public override string Describe() =>
        $"Names several of {Noun ?? $"what {Member} lists"}, separated by commas{(None is null ? "" : $"; empty for {None}")}. The Inspector ticks them from a list and flags a name that isn't one.";

    /// <summary>What <paramref name="component"/> lists to pick from now.</summary>
    public IReadOnlyList<string> Choices(Component component) => new ChoiceOfAttribute(Member).Choices(component);

    /// <summary>The names <paramref name="value"/> holds, in order.</summary>
    public static IReadOnlyList<string> Read(object? value) =>
        (value as string ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public override string? Problem(Component component, object? value)
    {
        var choices = Choices(component);
        if (choices.Count == 0)
            return null;
        var bad = Read(value).Where(n => !choices.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        return bad.Count == 0 ? null : $"There's no {string.Join(" or ", bad.Select(b => $"\"{b}\""))} to pick: the choices are {string.Join(", ", choices)}";
    }
}
