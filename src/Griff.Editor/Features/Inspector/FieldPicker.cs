using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// The list a field's ▾ button opens: what the field can name (the scene's entities, or the project's prefabs, scenes,
/// input actions, layers or messages) or the project's images or sounds, narrowed to those matching what's typed in
/// <see cref="Search"/>. Up and Down move <see cref="Selected"/>, Enter picks it (<see cref="PickSelected"/>), and a
/// field naming several keeps the list open to tick more. Typing a name it doesn't have offers to use it as typed.
/// </summary>
public sealed class FieldPicker : ObservableObject
{
    private readonly IReadOnlyList<PickerChoice> _all;
    private readonly Action<string> _pick;
    private readonly Func<string, bool> _isPicked;
    private readonly Action _close;
    private string _search = "";
    private IReadOnlyList<PickerChoice> _matches;
    private PickerChoice? _selected;

    internal FieldPicker(PickerSetup setup, IReadOnlyList<PickerChoice> choices, Action<string> pick, Func<string, bool> isPicked, Action close)
    {
        Setup = setup;
        _all = choices;
        _pick = pick;
        _isPicked = isPicked;
        _close = close;
        foreach (var choice in _all)
            choice.IsPicked = isPicked(choice.Name);
        _matches = _all;
        // Start on what's picked now, so Enter keeps it.
        _selected = _all.FirstOrDefault(c => c.IsPicked) ?? _all.FirstOrDefault();
    }

    internal PickerSetup Setup { get; }

    /// <summary>What the search box says while it's empty, e.g. "Type to find a prefab".</summary>
    public string Placeholder => Setup.Placeholder;

    /// <summary>True for a field naming several: picking one ticks it (or unticks it), and the list stays open.</summary>
    public bool Many => Setup.Many;

    /// <summary>What's typed: the list shows the choices whose names contain it, best match first and selected.</summary>
    public string Search
    {
        get => _search;
        set
        {
            if (!SetProperty(ref _search, value ?? ""))
                return;
            var text = _search.Trim();
            Matches = text.Length == 0
                ? _all
                : [.. _all.Select(c => (Choice: c, Rank: Rank(c, text))).Where(m => m.Rank >= 0).OrderBy(m => m.Rank).Select(m => m.Choice)];
            Selected = Matches.FirstOrDefault();
            OnPropertyChanged(nameof(TypedOffer));
            OnPropertyChanged(nameof(HasTypedOffer));
        }
    }

    // Lower is better: the name itself, then names starting with it, then a word in it starting with it, then containing it.
    private static int Rank(PickerChoice choice, string text)
    {
        foreach (var name in new[] { choice.Name, choice.Label })
        {
            if (name.Equals(text, StringComparison.OrdinalIgnoreCase))
                return 0;
        }

        var best = -1;
        foreach (var name in new[] { choice.Label, choice.Name })
        {
            var at = name.IndexOf(text, StringComparison.OrdinalIgnoreCase);
            var rank = at < 0 ? -1 : at == 0 ? 1 : char.IsLetterOrDigit(name[at - 1]) ? 3 : 2;
            if (rank >= 0 && (best < 0 || rank < best))
                best = rank;
        }

        return best;
    }

    /// <summary>The choices matching <see cref="Search"/>, best first (all of them, in their own order, while it's empty).</summary>
    public IReadOnlyList<PickerChoice> Matches
    {
        get => _matches;
        private set
        {
            if (SetProperty(ref _matches, value))
            {
                OnPropertyChanged(nameof(Note));
                OnPropertyChanged(nameof(HasNote));
            }
        }
    }

    /// <summary>The choice Enter picks.</summary>
    public PickerChoice? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    /// <summary>Said in place of the list when it's empty: "No prefabs in the project yet", or "Nothing matches".</summary>
    public string? Note => Matches.Count > 0 ? null : _all.Count == 0 ? Setup.Empty : "Nothing matches";

    public bool HasNote => Note is not null;

    /// <summary>
    /// "Use "Coin"" when what's typed isn't one of the choices and the field can take any name (a new layer or
    /// message); null otherwise. Enter uses it when nothing matches.
    /// </summary>
    public string? TypedOffer =>
        Setup.TakesTyped && _search.Trim() is { Length: > 0 } typed && !_all.Any(c => c.Name.Equals(typed, StringComparison.OrdinalIgnoreCase))
            ? $"Use \"{typed}\""
            : null;

    public bool HasTypedOffer => TypedOffer is not null;

    /// <summary>What else it can do, under the list: Paint a New One..., New Sound..., None.</summary>
    public IReadOnlyList<PickerAction> Actions => Setup.Actions;

    public bool HasActions => Actions.Count > 0;

    /// <summary>Moves <see cref="Selected"/> through the matches, by <paramref name="by"/> (negative goes up).</summary>
    public void MoveSelection(int by)
    {
        if (Matches.Count == 0)
            return;
        var at = Selected is null ? -1 : IndexOf(Selected);
        Selected = Matches[Math.Clamp(at < 0 ? (by > 0 ? 0 : Matches.Count - 1) : at + by, 0, Matches.Count - 1)];
    }

    private int IndexOf(PickerChoice choice)
    {
        for (var i = 0; i < Matches.Count; i++)
        {
            if (ReferenceEquals(Matches[i], choice))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Picks <see cref="Selected"/>, or the name typed when nothing matches and the field takes any name. Closes the list
    /// unless the field names several.
    /// </summary>
    public void PickSelected()
    {
        if (Selected is { } selected)
            Pick(selected.Name);
        else if (TypedOffer is not null)
            UseTyped();
    }

    /// <summary>Uses what's typed as the name, as it is.</summary>
    public void UseTyped()
    {
        if (_search.Trim() is { Length: > 0 } typed)
            Pick(typed);
    }

    /// <summary>
    /// Picks the choice called <paramref name="name"/> (or names it as typed): sets the field, or on one naming several,
    /// ticks it or unticks it and stays open.
    /// </summary>
    public void Pick(string name)
    {
        _pick(name);
        foreach (var choice in _all)
            choice.IsPicked = _isPicked(choice.Name);
        if (!Many)
            _close();
    }

    /// <summary>Does one of the <see cref="Actions"/>, by its label ("None") or tag ("picker:none"), and closes the list.</summary>
    public void Do(string action)
    {
        var found = Actions.FirstOrDefault(a => a.Label.Trim('.').Equals(action.Trim('.'), StringComparison.OrdinalIgnoreCase) || a.Tag == action)
            ?? throw new ArgumentException($"There's no {action}; it can do {string.Join(", ", Actions.Select(a => a.Label))}.");
        _close();
        found.Run();
    }

    /// <summary>Shuts the list.</summary>
    public void Close() => _close();
}

/// <summary>What kind of list a field's picker is: what it says, whether it ticks several, and what else it can do.</summary>
internal sealed record PickerSetup(string Placeholder, string Empty, bool Many, bool TakesTyped, IReadOnlyList<PickerAction> Actions);

/// <summary>
/// One thing a picker lists: its <see cref="Name"/> (what the field is set to), how it reads, a word on it, and a
/// picture or icon.
/// </summary>
public sealed class PickerChoice(string name, string? label = null, string? detail = null, int depth = 0, string? icon = null,
    Func<ImageSource?>? thumbnail = null) : ObservableObject
{
    private bool _isPicked;
    private ImageSource? _thumbnail;
    private bool _thumbnailRead;

    /// <summary>What picking it sets the field to: an entity's or prefab's name, an image's path.</summary>
    public string Name { get; } = name;

    /// <summary>How it reads in the list; its <see cref="Name"/> unless that's a path.</summary>
    public string Label { get; } = label ?? name;

    /// <summary>A word on it, shown faded on the right: "sent" for a message nothing hears yet, a sound's folder.</summary>
    public string? Detail { get; } = detail;

    /// <summary>How deep it is (an entity inside another is indented under it).</summary>
    public int Depth { get; } = depth;

    public Thickness Indent => new(Depth * 12, 0, 0, 0);

    /// <summary>An icon glyph to show before it, or null.</summary>
    public string? Icon { get; } = icon;

    /// <summary>A small picture of it (an image's), read when it's first shown.</summary>
    public ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbnailRead)
            {
                _thumbnailRead = true;
                _thumbnail = thumbnail?.Invoke();
            }

            return _thumbnail;
        }
    }

    public bool HasThumbnail => thumbnail is not null;

    /// <summary>True when the field names it now (on every selected entity, when there are several).</summary>
    public bool IsPicked
    {
        get => _isPicked;
        internal set => SetProperty(ref _isPicked, value);
    }

    public override string ToString() => IsPicked ? $"{Label} (picked)" : Label;
}

/// <summary>Something a picker does besides picking: "Paint a New One...", "New Sound...", "None".</summary>
public sealed record PickerAction(string Label, string Icon, string Tag, Action Run)
{
    public override string ToString() => Label;
}
