using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Scenes;
using Griff.Editor.Features.Art;
using Griff.Editing;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// Several names picked from what the component lists (<see cref="ChoicesOfAttribute"/>), e.g. an AI fighter's moves from
/// its fighter's: each ticked or not, in the list's order, and any it names that isn't one flagged.
/// </summary>
public sealed class ChoicesEditor(PropertyField field, ChoicesOfAttribute attribute) : FieldEditor<ChoicesOfAttribute>(field, attribute)
{
    /// <summary>What can be ticked: what the component lists, then what it names that isn't among them.</summary>
    public IReadOnlyList<ChoiceTick> Ticks
    {
        get
        {
            var picked = ChoicesOfAttribute.Read(Text);
            var choices = Attribute.Choices(Component);
            return [.. choices.Concat(picked.Where(p => !choices.Contains(p, StringComparer.OrdinalIgnoreCase)))
                .Select(c => new ChoiceTick(this, c, picked.Contains(c, StringComparer.OrdinalIgnoreCase), !choices.Contains(c, StringComparer.OrdinalIgnoreCase)))];
        }
    }

    /// <summary>"Every one" when none is ticked, as the field says, or how many are.</summary>
    public string Summary => ChoicesOfAttribute.Read(Text).Count == 0 ? $"None ticked: {Attribute.None ?? "none"}" : "";

    /// <summary>True when there's nothing to tick yet.</summary>
    public bool IsEmpty => Attribute.Choices(Component).Count == 0;

    /// <summary>Ticks <paramref name="name"/> (or unticks it), as one undo step, keeping them in the list's order.</summary>
    public void Tick(string name, bool on)
    {
        var choices = Attribute.Choices(Component);
        var picked = ChoicesOfAttribute.Read(Text).ToList();
        var known = choices.FirstOrDefault(c => c.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (on && known is null)
            throw new ArgumentException($"There's no {name} to tick; the choices are {string.Join(", ", choices)}.", nameof(name));
        picked.RemoveAll(p => p.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (on)
            picked.Add(known!);
        var ordered = picked.OrderBy(p => choices.ToList().FindIndex(c => c.Equals(p, StringComparison.OrdinalIgnoreCase)) is var i and >= 0 ? i : int.MaxValue);
        SetText(string.Join(", ", ordered));
    }
}

/// <summary>One name of a <see cref="ChoicesEditor"/>: ticked or not.</summary>
public sealed class ChoiceTick(ChoicesEditor owner, string name, bool ticked, bool unknown) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>True when it isn't one of the choices (flagged; unticking takes it out).</summary>
    public bool IsMissing { get; } = unknown;

    /// <summary>Ticked: it's one of those named. Setting it is one undo step.</summary>
    public bool IsTicked
    {
        get => ticked;
        set
        {
            if (value != ticked)
                owner.Tick(Name, value);
        }
    }

    /// <summary>"editor:tick:Jab": what demos find it by.</summary>
    public string Tag => $"editor:tick:{Name}";
}

/// <summary>
/// Something in another scene (<see cref="InSceneAttribute"/>), e.g. where a walker arrives: that scene's hotspots and other
/// entities to pick from, and a name it doesn't have flagged.
/// </summary>
public sealed class InSceneEditor(PropertyField field, InSceneAttribute attribute) : FieldEditor<InSceneAttribute>(field, attribute)
{
    /// <summary>What it says for none, first in the list.</summary>
    public const string None = "(where that scene has it)";

    /// <summary>The scene named, or null.</summary>
    public string? Scene => Attribute.SceneOf(Component);

    /// <summary>The scene's hotspots and entities, after none.</summary>
    public IReadOnlyList<string> Choices
    {
        get
        {
            var found = Host.Names?.EntitiesIn(Scene) ?? [];
            var current = Text.Trim();
            return [None, .. found, .. current.Length > 0 && !found.Contains(current, StringComparer.OrdinalIgnoreCase) ? [current] : Array.Empty<string>()];
        }
    }

    /// <summary>What it names (<see cref="None"/> for nothing); setting it is one undo step.</summary>
    public string Value
    {
        get => Field.IsMixed ? "" : Text.Trim() is { Length: > 0 } name ? name : None;
        set
        {
            if (value is null || value == Value)
                return;
            SetText(value == None ? null : value);
        }
    }

    /// <summary>Why it's flagged: the scene has nothing called that; or empty.</summary>
    public string Problem
    {
        get
        {
            if (Scene is not { } scene || Text.Trim() is not { Length: > 0 } name || Host.Names is not { } names)
                return "";
            var found = names.EntitiesIn(scene);
            return found.Count == 0 ? $"There's no scene called {scene} to look in."
                : found.Contains(name, StringComparer.OrdinalIgnoreCase) ? "" : $"{scene} has nothing called {name}.";
        }
    }

    public bool HasProblem => Problem.Length > 0;
}

/// <summary>
/// A number of a target's components (<see cref="StatAttribute"/>), e.g. what a card's buff raises: the numbers of the
/// components in the scene on things its targets are (a buff's: cards and what has health) to pick, or one typed.
/// </summary>
public sealed class StatEditor(PropertyField field, StatAttribute attribute) : FieldEditor<StatAttribute>(field, attribute)
{
    /// <summary>The value as typed; setting it is one undo step.</summary>
    public string Value
    {
        get => Text;
        set => SetText(value);
    }

    /// <summary>
    /// "Value" (any component's), then each number of the components on the scene's things its targets are (on anything
    /// for a stat without <see cref="StatAttribute.Targets"/>), and of the targets themselves: "Health.Max".
    /// </summary>
    public IReadOnlyList<string> Choices
    {
        get
        {
            var entities = Component.Entity?.Scene?.Entities.SelectMany(e => e.SelfAndDescendants()) ?? Component.Entity?.SelfAndDescendants() ?? [];
            var targets = Attribute.Targets;
            var types = entities.Where(e => targets.Count == 0 || e.Components.Any(c => targets.Any(t => t.IsInstanceOfType(c))))
                .SelectMany(e => e.Components).Select(c => c.GetType()).Concat(targets)
                .Where(t => t != typeof(Transform));
            return ["Value", .. FieldValues.StatNumbers(types)];
        }
    }

    /// <summary>Picks one of <see cref="Choices"/>.</summary>
    public string? Pick
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                SetText(value);
            OnPropertyChanged();
        }
    }
}
