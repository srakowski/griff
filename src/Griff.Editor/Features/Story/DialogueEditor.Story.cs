using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;
using Griff.Editing;
using Griff.Modules.VisualNovels;
using Griff.Scenes;

namespace Griff.Editor.Features.Story;

// The Dialogue Editor's story side, for any dialogue (a stage or not): the line picked's flags and messages (the [if],
// [if not], [set], [clear] and [send] tags), picked from those the project has; where a choice or a go-to goes, picked
// from the dialogue's parts; and choices, parts and go-tos added. Each change rewrites the line as one undo step.
public sealed partial class DialogueEditor
{
    /// <summary>What <see cref="GoesTo"/> calls a choice going on after the choices.</summary>
    public const string GoesOn = "(goes on)";

    /// <summary>The kinds of tag a line can have for flags and messages, as <see cref="AddTag"/> takes them.</summary>
    public IReadOnlyList<string> TagKinds { get; } = ["if", "if not", "set", "clear", "send"];

    private string _newTagKind = "set";
    private string? _newTagName;
    private RelayCommand? _addTag, _addChoice, _addPart, _addGoTo;
    private RelayCommand<StoryTagRow>? _removeTag;

    /// <summary>The line picked's flags and messages: its [if], [if not], [set], [clear] and [send] tags, in order.</summary>
    public ObservableCollection<StoryTagRow> StoryTags { get; } = [];

    /// <summary>The kind of tag <see cref="AddTagCommand"/> adds: if, if not, set, clear or send.</summary>
    public string NewTagKind
    {
        get => _newTagKind;
        set
        {
            if (SetProperty(ref _newTagKind, value))
                OnPropertyChanged(nameof(NewTagNames));
        }
    }

    /// <summary>The flag or message <see cref="AddTagCommand"/> adds the tag for.</summary>
    public string? NewTagName
    {
        get => _newTagName;
        set => SetProperty(ref _newTagName, value);
    }

    /// <summary>Picks <see cref="NewTagName"/> from <see cref="NewTagNames"/> (the list beside its box).</summary>
    public string? NewTagPick
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                NewTagName = value;
            OnPropertyChanged();
        }
    }

    /// <summary>What the new tag can name: the flags the project sets (for if, set and clear) or its messages (for send).</summary>
    public IReadOnlyList<string> NewTagNames => NamesFor(NewTagKind);

    /// <summary>True when the line picked can have flags and messages: anything but "# Part".</summary>
    public bool CanHaveTags => HasLine && !IsPartRow(Dialogue!.Lines[SelectedIndex]);

    /// <summary>True when the line picked is a choice or a go-to, which goes somewhere.</summary>
    public bool CanGoTo => HasLine && (IsGoToRow(Dialogue!.Lines[SelectedIndex]) || StageLines.Split(Dialogue.Lines[SelectedIndex]).Lead.Length > 0);

    /// <summary>
    /// Where the line picked goes: for a choice, a part, End, or <see cref="GoesOn"/> (on after the choices); for a go-to, a
    /// part or End. Setting it rewrites the line, as one undo step.
    /// </summary>
    public string GoesTo
    {
        get
        {
            if (!HasLine)
                return "";
            var row = Dialogue!.Lines[SelectedIndex];
            if (IsGoToRow(row))
                return SplitGoTo(row).To;
            var parts = StageLines.Split(row);
            return parts.Lead.Length > 0 && Arrow(parts.Text) is var (_, to) && to.Length > 0 ? to : GoesOn;
        }
        set
        {
            if (!HasLine || string.IsNullOrWhiteSpace(value) || value == GoesTo)
                return;
            var row = Dialogue!.Lines[SelectedIndex];
            if (IsGoToRow(row))
            {
                if (value == GoesOn)
                    throw new ArgumentException("A go-to has to go somewhere: a part or End.", nameof(value));
                var (tags, _) = SplitGoTo(row);
                SetLine(SelectedIndex, string.Join(' ', tags.Append("-> " + value.Trim())));
                return;
            }

            if (StageLines.Split(row).Lead.Length == 0)
                throw new InvalidOperationException("Only a choice or a go-to goes somewhere: pick one.");
            Change(p => p with { Text = Arrow(p.Text).Text + (value == GoesOn ? "" : " -> " + value.Trim()) }, "Edit Choice");
        }
    }

    /// <summary>Where a choice or go-to can go: the dialogue's parts and End (and, for a choice, <see cref="GoesOn"/>).</summary>
    public IReadOnlyList<string> GoToChoices
    {
        get
        {
            var parts = Dialogue?.PartNames() ?? [];
            var choices = new List<string>();
            if (HasLine && !IsGoToRow(Dialogue!.Lines[SelectedIndex]))
                choices.Add(GoesOn);
            choices.AddRange(parts);
            choices.Add("End");
            if (GoesTo is { Length: > 0 } now && !choices.Contains(now, StringComparer.OrdinalIgnoreCase))
                choices.Add(now);
            return Kept(choices);
        }
    }

    /// <summary>
    /// Adds a tag for a flag or message to the line picked, after its others, as one undo step: <paramref name="kind"/> is
    /// "if" (said only while the flag is set), "if not", "set", "clear" or "send" (a message). Returns the tag, e.g.
    /// "[set MetKing]".
    /// </summary>
    public string AddTag(string kind, string name)
    {
        if (!CanHaveTags)
            throw new InvalidOperationException(HasLine ? "A part can't have tags: put them on the line after it." : "Pick a line to add a tag to.");
        var word = kind.Trim().ToLowerInvariant();
        if (!TagKinds.Contains(word))
            throw new ArgumentException($"A tag is {string.Join(", ", TagKinds)}, not \"{kind}\".", nameof(kind));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(word == "send" ? "Name the message it sends." : "Name the flag.", nameof(name));
        var tag = $"[{word} {name.Trim()}]";
        var index = SelectedIndex;
        var row = Dialogue!.Lines[index];
        if (IsGoToRow(row))
        {
            var (tags, to) = SplitGoTo(row);
            SetLine(index, string.Join(' ', tags.Append(tag).Append("-> " + to)));
        }
        else
        {
            Change(p => p with { Tags = [.. p.Tags, tag] }, "Add Tag");
        }

        return tag;
    }

    /// <summary>Removes the line picked's flag or message tag number <paramref name="index"/> (in <see cref="StoryTags"/>), as one undo step.</summary>
    public void RemoveTag(int index)
    {
        if (index < 0 || index >= StoryTags.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"The line has {StoryTags.Count} flags and messages, numbered from 0.");
        var tag = StoryTags[index].Tag;
        var row = Dialogue!.Lines[SelectedIndex];
        if (IsGoToRow(row))
        {
            var (tags, to) = SplitGoTo(row);
            var kept = tags.ToList();
            kept.RemoveAt(kept.FindIndex(t => t == tag));
            SetLine(SelectedIndex, string.Join(' ', kept.Append("-> " + to)));
            return;
        }

        Change(p =>
        {
            var tags = p.Tags.ToList();
            tags.RemoveAt(tags.FindIndex(t => t == tag));
            return p with { Tags = tags };
        }, "Remove Tag");
    }

    /// <summary>Adds a choice after the line picked ("&gt; ..."), offered under the line before it, and picks it. Returns its number.</summary>
    public int AddChoice(string text = "...", string? goesTo = null) =>
        AddLine("> " + (string.IsNullOrWhiteSpace(text) ? "..." : text.Trim()) + (string.IsNullOrWhiteSpace(goesTo) || goesTo == GoesOn ? "" : " -> " + goesTo.Trim()));

    /// <summary>Adds a part after the line picked ("# Name", a new name for none), which choices and go-tos can go to, and picks it.</summary>
    public int AddPart(string? name = null)
    {
        var parts = Dialogue?.PartNames() ?? [];
        var part = string.IsNullOrWhiteSpace(name)
            ? Enumerable.Range(parts.Count + 1, 100).Select(i => $"Part {i}").First(n => !parts.Contains(n, StringComparer.OrdinalIgnoreCase))
            : name.Trim();
        return AddLine("# " + part);
    }

    /// <summary>Adds a go-to after the line picked ("-&gt; End" for none), and picks it.</summary>
    public int AddGoTo(string? to = null) => AddLine("-> " + (string.IsNullOrWhiteSpace(to) ? "End" : to.Trim()));

    public IRelayCommand AddTagCommand => _addTag ??= new RelayCommand(() => AddTag(NewTagKind, NewTagName ?? ""), () => CanHaveTags);

    public IRelayCommand<StoryTagRow> RemoveTagCommand => _removeTag ??= new RelayCommand<StoryTagRow>(row => { if (row is not null) RemoveTag(row.Index); });

    public IRelayCommand AddChoiceCommand => _addChoice ??= new RelayCommand(() => AddChoice(), () => Dialogue is not null);

    public IRelayCommand AddPartCommand => _addPart ??= new RelayCommand(() => AddPart(), () => Dialogue is not null);

    public IRelayCommand AddGoToCommand => _addGoTo ??= new RelayCommand(() => AddGoTo(), () => Dialogue is not null);

    // Shows the line picked's flags, messages and where it goes.
    private void NotifyStory()
    {
        StoryTags.Clear();
        if (HasLine && !IsPartRow(Dialogue!.Lines[SelectedIndex]))
        {
            var row = Dialogue.Lines[SelectedIndex];
            var tags = IsGoToRow(row) ? SplitGoTo(row).Tags : StageLines.Split(row).Tags;
            var i = 0;
            foreach (var tag in tags)
            {
                if (Describe(tag) is { } words)
                    StoryTags.Add(new StoryTagRow(i++, tag, words));
            }
        }

        OnPropertyChanged(nameof(CanHaveTags));
        OnPropertyChanged(nameof(CanGoTo));
        OnPropertyChanged(nameof(GoesTo));
        OnPropertyChanged(nameof(GoToChoices));
        OnPropertyChanged(nameof(NewTagNames));
        _addTag?.NotifyCanExecuteChanged();
        _addChoice?.NotifyCanExecuteChanged();
        _addPart?.NotifyCanExecuteChanged();
        _addGoTo?.NotifyCanExecuteChanged();
    }

    // The flags the project sets, or its messages, for a tag of that kind.
    private IReadOnlyList<string> NamesFor(string kind)
    {
        if (_projectSession.Names is not { } names)
            return [];
        return kind == "send"
            ? [.. names.Choices(new NamesAttribute(NameKind.Message)).Select(c => c.Name)]
            : [.. names.Flags.Order(StringComparer.OrdinalIgnoreCase)];
    }

    // "[set MetKing]" in words: "Sets the flag MetKing"; null for a tag that isn't a flag's or a message's (a direction).
    private static string? Describe(string tag)
    {
        var inside = tag.Trim().TrimStart('[').TrimEnd(']').Trim();
        var space = inside.IndexOf(' ');
        if (space < 0)
            return null;
        var (word, name) = (inside[..space].ToLowerInvariant(), inside[(space + 1)..].Trim());
        if (word == "if" && name.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
            return $"Only while the flag {name[4..].Trim()} isn't set";
        return word switch
        {
            "if" => $"Only while the flag {name} is set",
            "set" => $"Sets the flag {name}",
            "clear" => $"Clears the flag {name}",
            "send" => $"Sends the message {name}",
            _ => null,
        };
    }

    private static bool IsPartRow(string? row) => (row ?? "").TrimStart().StartsWith('#');

    // "-> Shop", or "[if HasKey] -> Shop": a row that goes somewhere by itself.
    private static bool IsGoToRow(string? row) => GoToRow().Match(row ?? "") is { Success: true };

    private static (IReadOnlyList<string> Tags, string To) SplitGoTo(string row)
    {
        var match = GoToRow().Match(row);
        return ([.. TagPattern().Matches(match.Groups[1].Value).Select(m => m.Value)], match.Groups[2].Value.Trim());
    }

    // A choice's text and where it goes: "Buy a sword -> Shop" as ("Buy a sword", "Shop").
    private static (string Text, string To) Arrow(string text)
    {
        var arrow = text.LastIndexOf("->", StringComparison.Ordinal);
        return arrow < 0 ? (text.Trim(), "") : (text[..arrow].Trim(), text[(arrow + 2)..].Trim());
    }

    [GeneratedRegex(@"^\s*((?:\[[^\]]+\]\s*)*)->(.*)$")]
    private static partial Regex GoToRow();

    [GeneratedRegex(@"\[[^\]]+\]")]
    private static partial Regex TagPattern();
}

/// <summary>One of a line's flags or messages in the Dialogue Editor: where it is among them, its tag and what it does.</summary>
public sealed record StoryTagRow(int Index, string Tag, string Description);
