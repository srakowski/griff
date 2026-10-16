using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Scenes;
using Griff.Editor.Features.Art;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// An animation's clips (<see cref="ClipsAttribute"/>), a row each: its name, its first and last frame on the sheet, its
/// speed (0 for the animation's own) and whether it plays once. Add puts a clip after the last one's frames; the Clip
/// Editor (Art > Animation Clips) shows them playing.
/// </summary>
public sealed class ClipsEditor : FieldEditor<ClipsAttribute>
{
    public ClipsEditor(PropertyField field, ClipsAttribute attribute)
        : base(field, attribute)
    {
        Refresh();
    }

    /// <summary>The clips, a row each.</summary>
    public ObservableCollection<ClipsRow> Rows { get; } = [];

    /// <summary>The clips as they are.</summary>
    public IReadOnlyList<AnimationClip> Clips => Animation.ParseClips(Text);

    /// <summary>
    /// Adds a clip called <paramref name="name"/> (or "Clip 2" and so on) of four frames after the last clip's, as one undo
    /// step. Returns its name.
    /// </summary>
    public string Add(string? name = null)
    {
        var clips = Clips.ToList();
        name = string.IsNullOrWhiteSpace(name) ? NewName(clips) : name.Trim().Replace(' ', '_');
        if (clips.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"There's a clip called {name} already.", nameof(name));
        var first = clips.Count > 0 ? clips.Max(c => c.FirstFrame + Math.Max(1, c.FrameCount)) : 0;
        clips.Add(new AnimationClip(name, first, 4));
        SetText(Animation.FormatClips(clips));
        return name;
    }

    /// <summary>Takes clip <paramref name="number"/> (from 1) out, as one undo step.</summary>
    public void Remove(int number)
    {
        var clips = Clips.ToList();
        Check(number, clips.Count);
        clips.RemoveAt(number - 1);
        SetText(Animation.FormatClips(clips));
    }

    /// <summary>
    /// Sets clip <paramref name="number"/> (from 1): its name, first and last frame, speed (0 for the animation's) and
    /// whether it plays once, as one undo step.
    /// </summary>
    public void Set(int number, string name, int first, int last, double speed = 0, bool once = false)
    {
        Change(number, c => new AnimationClip(Name(name, c), Math.Min(first, last), Math.Abs(last - first) + 1,
            speed > 0 ? (float)speed : null, once ? false : c.Loop == true ? true : null), seal: true);
    }

    /// <summary>Opens the Clip Editor on the selected entity, to see its clips play and pick their frames off the sheet.</summary>
    public void OpenClipEditor() => Host.RunCommand<OpenClipEditorCommand>();

    public IRelayCommand AddCommand => _add ??= new RelayCommand(() => Add());

    public IRelayCommand OpenClipEditorCommand => _open ??= new RelayCommand(OpenClipEditor);

    private IRelayCommand? _add, _open;

    public override void Refresh()
    {
        Fit(Rows, Clips.Count, () => new ClipsRow(this));
        OnPropertyChanged(string.Empty);
    }

    internal void Change(int number, Func<AnimationClip, AnimationClip> change, bool seal)
    {
        var clips = Clips.ToList();
        if (number < 1 || number > clips.Count)
            return;
        clips[number - 1] = change(clips[number - 1]);
        var text = Animation.FormatClips(clips);
        if (seal)
            SetText(text);
        else
            SetTextLive(text);
    }

    // A clip's name is one word: spaces would split it.
    private static string Name(string name, AnimationClip clip) => string.IsNullOrWhiteSpace(name) ? clip.Name : name.Trim().Replace(' ', '_');

    private static string NewName(List<AnimationClip> clips)
    {
        var n = clips.Count + 1;
        while (clips.Any(c => c.Name.Equals($"Clip{n}", StringComparison.OrdinalIgnoreCase)))
            n++;
        return $"Clip{n}";
    }

    private static void Check(int number, int count)
    {
        if (number < 1 || number > count)
            throw new ArgumentOutOfRangeException(nameof(number), count == 0 ? "There are no clips yet: Add makes one." : $"The clips are numbered 1 to {count}.");
    }
}

/// <summary>One clip of a <see cref="ClipsEditor"/>.</summary>
public sealed class ClipsRow(ClipsEditor owner) : EditorRow(owner.Remove)
{
    private AnimationClip Clip => owner.Clips.ElementAtOrDefault(Number - 1);

    /// <summary>Its name, one word ("Run"): what an animation's Clip and a script's Play name.</summary>
    public string Name
    {
        get => Clip.Name ?? "";
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                owner.Change(Number, c => c with { Name = value.Trim().Replace(' ', '_') }, seal: true);
        }
    }

    /// <summary>Its first frame on the sheet, counting from 0.</summary>
    public double First
    {
        get => Clip.FirstFrame;
        set => owner.Change(Number, c => c with { FirstFrame = Math.Max(0, (int)Math.Round(value)), FrameCount = Math.Max(1, c.FirstFrame + c.FrameCount - Math.Max(0, (int)Math.Round(value))) }, seal: false);
    }

    /// <summary>Its last frame on the sheet.</summary>
    public double Last
    {
        get => Clip.FirstFrame + Math.Max(1, Clip.FrameCount) - 1;
        set => owner.Change(Number, c => c with { FrameCount = Math.Max(1, (int)Math.Round(value) - c.FirstFrame + 1) }, seal: false);
    }

    /// <summary>Frames a second; 0 plays at the animation's own speed.</summary>
    public double Speed
    {
        get => Clip.FramesPerSecond ?? 0;
        set => owner.Change(Number, c => c with { FramesPerSecond = value > 0 ? (float)value : null }, seal: false);
    }

    /// <summary>Plays once and stops on its last frame, rather than looping.</summary>
    public bool Once
    {
        get => Clip.Loop == false;
        set => owner.Change(Number, c => c with { Loop = value ? false : c.Loop == true ? true : null }, seal: true);
    }
}

/// <summary>
/// A name picked from what its component lists (<see cref="ChoiceOfAttribute"/>), e.g. an animation's starting clip from
/// its clips: a list of them, or none.
/// </summary>
public sealed class ChoiceEditor(PropertyField field, ChoiceOfAttribute attribute) : FieldEditor<ChoiceOfAttribute>(field, attribute)
{
    /// <summary>What it says for none, first in the list.</summary>
    public const string None = "(none)";

    /// <summary>What can be picked: none, then what the component lists now (and what it names, if that isn't one of them).</summary>
    public IReadOnlyList<string> Choices
    {
        get
        {
            var choices = Attribute.Choices(Component).ToList();
            if (Text.Trim() is { Length: > 0 } current && !choices.Contains(current, StringComparer.OrdinalIgnoreCase))
                choices.Add(current);
            return [None, .. choices];
        }
    }

    /// <summary>What it names (<see cref="None"/> for nothing); setting it picks another, as one undo step.</summary>
    public string Value
    {
        get => Field.IsMixed ? "" : Text.Trim() is { Length: > 0 } name
            ? Choices.FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name
            : None;
        set
        {
            if (value is null || value == Value)
                return;
            if (value != None && !Attribute.Choices(Component).Contains(value, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"There's no {value} to pick; the choices are {string.Join(", ", Attribute.Choices(Component))}.");
            SetText(value == None ? null : value);
        }
    }
}
