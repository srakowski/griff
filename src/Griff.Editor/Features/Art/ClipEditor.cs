using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Editing;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Workspace;
using Griff.Scenes;
using Animation = Griff.Scenes.Animation;

namespace Griff.Editor.Features.Art;

/// <summary>
/// The Clip Editor: the selected entity's animation clips, listed with their frames, speed and looping, and the one
/// picked playing in a preview the way the game plays it. Clips are added, renamed and removed here, and one is picked
/// for the game to start with, each as one undo step. It follows the selection, so it stays open beside the scene.
/// </summary>
public sealed class ClipEditor : ObservableObject
{
    /// <summary>What the list calls an animation's own frames (its First Frame and Frame Count), which it plays without a clip.</summary>
    public const string OwnFrames = "(own frames)";

    private const int MaxStrip = 100;

    private static readonly string[] SuggestedNames = ["Idle", "Run", "Jump", "Fall", "Attack", "Hurt", "Die"];

    private readonly IWorkspace _workspace;
    private readonly ISelection _selection;
    private readonly EditorArt _art;
    private readonly IDialogService _dialogs;
    private readonly Dictionary<int, BitmapSource> _frameImages = [];
    private (BitmapSource? Image, int Columns, int Rows) _sheet;
    private bool _sheetRead;
    private SceneDocument? _document;
    private Entity? _entity;
    private ClipRow? _selected;
    private ImageSource? _frameImage;
    private string? _notice;
    private bool _isPlaying = true;
    private double _time;
    private int _frame;
    private ClipEditorWindow? _window;

    public ClipEditor(IWorkspace workspace, ISelection selection, EditorArt art, IDialogService dialogs)
    {
        _workspace = workspace;
        _selection = selection;
        _art = art;
        _dialogs = dialogs;

        PlayCommand = new RelayCommand(() => { if (IsPlaying) Pause(); else Play(); }, () => Selected is not null);
        StepBackCommand = new RelayCommand(() => Step(-1), () => Selected is not null);
        StepForwardCommand = new RelayCommand(() => Step(1), () => Selected is not null);
        AddClipCommand = new RelayCommand(() => AddClip(), () => Animation is not null);
        RenameClipCommand = new RelayCommand(() => RenameClip(), () => Selected is { IsOwnFrames: false });
        RemoveClipCommand = new RelayCommand(RemoveClip, () => Selected is { IsOwnFrames: false });
        StartWithClipCommand = new RelayCommand(StartWithClip, () => Selected is { IsStart: false });
        AddAnimationCommand = new RelayCommand(AddAnimation, () => CanAddAnimation);
        ShowFrameCommand = new RelayCommand<ClipFrame>(frame => { if (frame is not null) ShowFrame(frame.Index); });

        // A picture saved over the sheet: show the new one.
        art.ImageSaved += (_, _) => Rebuild();
        selection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ISelection.Entity))
                Follow();
        };
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IWorkspace.Scene))
                Track(workspace.Scene);
        };
        Track(workspace.Scene);
    }

    public string Title => "Clip Editor";

    /// <summary>The entity whose clips are listed: the primary selected one.</summary>
    public Entity? Entity => _entity;

    public string EntityName => _entity?.Name ?? "";

    public Animation? Animation => _entity?.GetComponent<Animation>();

    public Sprite? Sprite => _entity?.GetComponent<Sprite>();

    /// <summary>
    /// The animation's clips. When it has none, or the game starts without one, its own frames are listed too (as
    /// <see cref="OwnFrames"/>), since that's what it plays then.
    /// </summary>
    public ObservableCollection<ClipRow> Clips { get; } = [];

    /// <summary>The clip being previewed.</summary>
    public ClipRow? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
                return;
            OnPropertyChanged(nameof(HasSelected));
            Restart();
            UpdateCommands();
        }
    }

    public bool HasSelected => Selected is not null;

    /// <summary>Why there's nothing (or not everything) to show, or null: nothing selected, no animation, no picture.</summary>
    public string? Notice
    {
        get => _notice;
        private set
        {
            if (SetProperty(ref _notice, value))
                OnPropertyChanged(nameof(HasNotice));
        }
    }

    public bool HasNotice => Notice is not null;

    /// <summary>True when the selected entity has a sprite but no animation to play its frames.</summary>
    public bool CanAddAnimation => _entity is not null && Animation is null && Sprite is not null;

    public bool HasAnimation => Animation is not null;

    /// <summary>The frame of the sprite sheet the preview shows.</summary>
    public ImageSource? FrameImage
    {
        get => _frameImage;
        private set => SetProperty(ref _frameImage, value);
    }

    /// <summary>The previewed clip's frames, as a strip under the preview; the one showing is marked.</summary>
    public ObservableCollection<ClipFrame> Frames { get; } = [];

    /// <summary>True while the preview plays. A clip that plays once stops on its last frame; playing it again starts over.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    /// <summary>Seconds into the previewed clip.</summary>
    public double Time => _time;

    /// <summary>Which of the clip's frames is showing, counting from 0.</summary>
    public int Frame => _frame;

    /// <summary>The sprite sheet's frame showing (what the sprite's Frame is set to), counting from 0.</summary>
    public int SheetFrame => Selected is { } clip ? clip.First + _frame : 0;

    /// <summary>Where the preview is, e.g. "Frame 2 of 4 (sheet frame 5)".</summary>
    public string FrameText => Selected is { } clip ? $"Frame {_frame + 1} of {clip.Count} (sheet frame {SheetFrame})" : "";

    public IRelayCommand PlayCommand { get; }

    public IRelayCommand StepBackCommand { get; }

    public IRelayCommand StepForwardCommand { get; }

    public IRelayCommand AddClipCommand { get; }

    public IRelayCommand RenameClipCommand { get; }

    public IRelayCommand RemoveClipCommand { get; }

    public IRelayCommand StartWithClipCommand { get; }

    public IRelayCommand AddAnimationCommand { get; }

    public IRelayCommand<ClipFrame> ShowFrameCommand { get; }

    /// <summary>The Clip Editor's window, while it's open.</summary>
    public ClipEditorWindow? Window => _window;

    /// <summary>Opens the Clip Editor's window (or brings it to the front).</summary>
    public void Open()
    {
        if (_window is null)
        {
            _window = new ClipEditorWindow(this) { Owner = Application.Current?.MainWindow };
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        else
            _window.Activate();
    }

    /// <summary>Previews the clip called <paramref name="name"/> (or <see cref="OwnFrames"/>).</summary>
    public void SelectClip(string name) =>
        Selected = Find(name) ?? throw new InvalidOperationException($"\"{EntityName}\" has no clip called \"{name}\"; its clips are {string.Join(", ", Clips.Select(c => c.Name))}.");

    public void Play()
    {
        if (Selected is not { } clip)
            return;
        if (clip.FramesPerSecond > 0 && !clip.Loop && _time * clip.FramesPerSecond >= clip.Count)
            _time = 0;
        IsPlaying = true;
        Show();
    }

    public void Pause() => IsPlaying = false;

    /// <summary>Pauses and moves <paramref name="frames"/> frames on (back, if negative), going round at the ends.</summary>
    public void Step(int frames)
    {
        if (Selected is not { } clip)
            return;
        ShowFrame(((_frame + frames) % clip.Count + clip.Count) % clip.Count);
    }

    /// <summary>Pauses on the clip's frame <paramref name="index"/>, counting from 0.</summary>
    public void ShowFrame(int index)
    {
        if (Selected is not { } clip)
            return;
        IsPlaying = false;
        index = Math.Clamp(index, 0, clip.Count - 1);
        _time = clip.FramesPerSecond > 0 ? (index + 0.5) / clip.FramesPerSecond : 0;
        Show(index);
    }

    /// <summary>Moves the preview on by <paramref name="seconds"/>. Called by the window every frame it's open.</summary>
    public void Tick(double seconds)
    {
        if (!IsPlaying || Selected is not { } clip)
            return;
        _time += seconds;
        if (!clip.Loop && clip.FramesPerSecond > 0 && _time * clip.FramesPerSecond >= clip.Count)
            IsPlaying = false;
        Show();
    }

    /// <summary>
    /// Adds a clip called <paramref name="name"/> (asked for when null), and previews it. The first clip takes the
    /// animation's own frames and becomes the one the game starts with; later ones take the frame after the last clip's.
    /// </summary>
    public void AddClip(string? name = null)
    {
        if (Animation is not { } animation)
            return;
        var clips = animation.ClipList.ToList();
        name ??= _dialogs.Prompt("Add Clip", "Clip name", SuggestName(), text => NameProblem(text, null));
        if (name is null)
            return;
        name = Valid(name, null);

        AnimationClip clip;
        if (clips.Count == 0)
            clip = new AnimationClip(name, Math.Max(0, animation.FirstFrame), Math.Max(1, animation.FrameCount));
        else
        {
            var next = clips.Max(c => c.FirstFrame + c.FrameCount);
            clip = new AnimationClip(name, SheetFrames > 1 && next >= SheetFrames ? 0 : next, 1);
        }

        clips.Add(clip);
        var start = string.IsNullOrEmpty(animation.Clip) && clips.Count == 1 ? name : animation.Clip;
        Change($"Add Clip {name}", clips, start);
        SelectClip(name);
    }

    /// <summary>Renames the previewed clip to <paramref name="name"/> (asked for when null); the starting clip follows.</summary>
    public void RenameClip(string? name = null)
    {
        if (Animation is not { } animation || Selected is not { IsOwnFrames: false } row)
            return;
        name ??= _dialogs.Prompt("Rename Clip", "Clip name", row.Name, text => NameProblem(text, row.Name));
        if (name is null)
            return;
        name = Valid(name, row.Name);
        if (name == row.Name)
            return;

        var clips = animation.ClipList.Select(c => Same(c.Name, row.Name) ? c with { Name = name } : c).ToList();
        var start = Same(animation.Clip, row.Name) ? name : animation.Clip;
        Change($"Rename Clip {row.Name} to {name}", clips, start);
        SelectClip(name);
    }

    /// <summary>Removes the previewed clip. If the game started with it, it starts with none (the animation's own frames).</summary>
    public void RemoveClip()
    {
        if (Animation is not { } animation || Selected is not { IsOwnFrames: false } row)
            return;
        var index = Clips.IndexOf(row);
        var clips = animation.ClipList.Where(c => !Same(c.Name, row.Name)).ToList();
        var start = Same(animation.Clip, row.Name) ? null : animation.Clip;
        Change($"Remove Clip {row.Name}", clips, start);
        Selected = Clips.Count == 0 ? null : Clips[Math.Clamp(index, 0, Clips.Count - 1)];
    }

    /// <summary>Makes the previewed clip the one the game starts with (the animation's Clip).</summary>
    public void StartWithClip()
    {
        if (Animation is not { } animation || Selected is not { } row)
            return;
        Change(row.IsOwnFrames ? "Start Without a Clip" : $"Start With {row.Name}", animation.ClipList, row.IsOwnFrames ? null : row.Name);
    }

    /// <summary>Gives the selected entity an Animation to play its sprite sheet's frames.</summary>
    public void AddAnimation()
    {
        if (!CanAddAnimation || _document is not { } document)
            return;
        var sprite = Sprite!;
        var frames = Math.Max(1, sprite.Columns) * Math.Max(1, sprite.Rows);
        document.History.Seal();
        document.Execute(new AddComponentEdit(_entity!, new Animation { FrameCount = frames }));
        document.History.Seal();
    }

    // ----- Following the selection

    private void Track(SceneDocument? document)
    {
        if (_document is not null)
            _document.Changed -= OnDocumentChanged;
        _document = document;
        if (_document is not null)
            _document.Changed += OnDocumentChanged;
        Follow();
    }

    private void OnDocumentChanged(object? sender, EditEventArgs e) => Rebuild();

    // A new entity: list its clips and preview the one the game starts with.
    private void Follow()
    {
        _entity = _selection.Entity;
        OnPropertyChanged(nameof(Entity));
        Rebuild(pickStart: true);
    }

    private void Rebuild() => Rebuild(pickStart: false);

    private void Rebuild(bool pickStart)
    {
        var keep = pickStart ? null : Selected?.Name;
        var animation = Animation;
        Clips.Clear();
        if (animation is not null)
        {
            var clips = animation.ClipList;
            var startsWithClip = clips.Any(c => Same(c.Name, animation.Clip));
            foreach (var clip in clips)
            {
                var (first, count, fps, loop) = animation.ClipSettings(clip.Name);
                Clips.Add(new ClipRow(clip.Name, first, count, fps, loop, clip.FramesPerSecond is not null, clip.Loop is not null,
                    false, Same(clip.Name, animation.Clip)));
            }

            if (clips.Count == 0 || !startsWithClip)
            {
                var (first, count, fps, loop) = animation.ClipSettings(null);
                Clips.Insert(0, new ClipRow(OwnFrames, first, Math.Max(1, count), fps, loop, false, false, true, true));
            }
        }

        _sheetRead = false;
        Notice = NoticeFor(animation);
        _selected = (keep is null ? null : Find(keep)) ?? Clips.FirstOrDefault(c => c.IsStart) ?? Clips.FirstOrDefault();
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelected));
        OnPropertyChanged(nameof(EntityName));
        OnPropertyChanged(nameof(Animation));
        OnPropertyChanged(nameof(Sprite));
        OnPropertyChanged(nameof(HasAnimation));
        OnPropertyChanged(nameof(CanAddAnimation));
        // The same clip carries on where it was (its frames may have changed, or the picture); another starts over.
        Restart(keepTime: keep is not null && Same(keep, _selected?.Name));
        UpdateCommands();
    }

    private string? NoticeFor(Animation? animation)
    {
        if (_entity is null)
            return "Select something with an Animation to see its clips.";
        if (animation is null)
            return Sprite is null
                ? $"{_entity.Name} has no Sprite or Animation."
                : $"{_entity.Name} has no Animation. Add one to play frames of its sprite sheet.";
        if (Sprite is not { } sprite)
            return $"{_entity.Name} has no Sprite to show its frames.";
        if (string.IsNullOrWhiteSpace(sprite.Image))
            return $"{_entity.Name}'s sprite has no picture yet: paint one (Art > Paint Selected) to see its frames.";
        if (_art.Thumbnail(sprite.Image) is null)
            return $"{_entity.Name}'s sprite's picture, {sprite.Image}, can't be found.";
        if (SheetFrames <= 1)
            return "Its sprite shows the whole picture as one frame: set the sprite's Columns and Rows to cut it into frames.";
        return null;
    }

    // ----- The preview

    private int SheetFrames => Sprite is { } sprite ? Math.Max(1, sprite.Columns) * Math.Max(1, sprite.Rows) : 1;

    private void Restart(bool keepTime = false)
    {
        if (!keepTime)
        {
            _time = 0;
            IsPlaying = Selected is not null;
        }

        Frames.Clear();
        if (Selected is { } clip)
        {
            // A clip longer than any sheet would hold (a typo) shows its first frames.
            for (var i = 0; i < Math.Min(clip.Count, MaxStrip); i++)
                Frames.Add(new ClipFrame(i, clip.First + i, SheetImage(clip.First + i)));
        }

        Show();
    }

    private void Show() =>
        Show(Selected is { } clip ? Griff.Scenes.Animation.FrameAt((float)_time, clip.Count, clip.FramesPerSecond, clip.Loop) : 0);

    private void Show(int index)
    {
        _frame = index;
        for (var i = 0; i < Frames.Count; i++)
            Frames[i].IsCurrent = i == index;
        FrameImage = Selected is { } clip ? SheetImage(clip.First + index) : null;
        OnPropertyChanged(nameof(Time));
        OnPropertyChanged(nameof(Frame));
        OnPropertyChanged(nameof(SheetFrame));
        OnPropertyChanged(nameof(FrameText));
    }

    // One frame of the sprite sheet, cut out the way the sprite cuts it (see Sprite.FrameRect).
    private BitmapSource? SheetImage(int frame)
    {
        if (Sprite is not { } sprite)
            return null;
        // The picture is read again when the clips are listed again (after any change), not every frame.
        var (columns, rows) = (Math.Max(1, sprite.Columns), Math.Max(1, sprite.Rows));
        if (!_sheetRead || _sheet.Columns != columns || _sheet.Rows != rows)
        {
            _sheet = (_art.Thumbnail(sprite.Image), columns, rows);
            _sheetRead = true;
            _frameImages.Clear();
        }

        if (_sheet.Image is not { } image)
            return null;
        var count = columns * rows;
        frame = (frame % count + count) % count;
        if (_frameImages.TryGetValue(frame, out var cached))
            return cached;

        var (width, height) = (image.PixelWidth / columns, image.PixelHeight / rows);
        if (width <= 0 || height <= 0)
            return null;
        var cut = new CroppedBitmap(image, new Int32Rect(frame % columns * width, frame / columns * height, width, height));
        cut.Freeze();
        return _frameImages[frame] = cut;
    }

    // ----- Changing the clips

    // Sets the animation's Clips (and Clip, the one it starts with) as one undo step.
    private void Change(string description, IReadOnlyList<AnimationClip> clips, string? start)
    {
        if (Animation is not { } animation || _document is not { } document)
            return;
        var properties = _workspace.Components.Get(typeof(Animation)).Properties;
        var text = clips.Count == 0 ? null : Griff.Scenes.Animation.FormatClips(clips);
        var edits = new List<IEdit>();
        if (text != animation.Clips)
            edits.Add(new SetPropertyEdit(animation, properties.Single(p => p.Name == nameof(Griff.Scenes.Animation.Clips)), text));
        if (!string.Equals(start ?? "", animation.Clip ?? "", StringComparison.Ordinal))
            edits.Add(new SetPropertyEdit(animation, properties.Single(p => p.Name == nameof(Griff.Scenes.Animation.Clip)), start));
        if (edits.Count == 0)
            return;
        document.History.Seal();
        document.Execute(new CompositeEdit(description, edits));
        document.History.Seal();
    }

    /// <summary>What's wrong with <paramref name="name"/> as a clip's name (renaming <paramref name="renaming"/>), or null.</summary>
    public string? NameProblem(string name, string? renaming)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return "A clip needs a name.";
        if (trimmed.Any(c => char.IsWhiteSpace(c) || c is ',' or ';' or ':' or '@'))
            return "A clip's name is one word, with no spaces, commas or colons.";
        if (Same(trimmed, OwnFrames))
            return $"\"{OwnFrames}\" is what the list calls the animation's own frames.";
        if (Animation?.ClipList.Any(c => Same(c.Name, trimmed) && !Same(c.Name, renaming)) == true)
            return $"There's already a clip called {trimmed}.";
        return null;
    }

    private string Valid(string name, string? renaming) =>
        NameProblem(name, renaming) is { } problem ? throw new InvalidOperationException(problem) : name.Trim();

    private string SuggestName()
    {
        foreach (var name in SuggestedNames)
        {
            if (NameProblem(name, null) is null)
                return name;
        }

        for (var i = 2; ; i++)
        {
            if (NameProblem($"Clip{i}", null) is null)
                return $"Clip{i}";
        }
    }

    private ClipRow? Find(string name) => Clips.FirstOrDefault(c => Same(c.Name, name));

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void UpdateCommands()
    {
        PlayCommand.NotifyCanExecuteChanged();
        StepBackCommand.NotifyCanExecuteChanged();
        StepForwardCommand.NotifyCanExecuteChanged();
        AddClipCommand.NotifyCanExecuteChanged();
        RenameClipCommand.NotifyCanExecuteChanged();
        RemoveClipCommand.NotifyCanExecuteChanged();
        StartWithClipCommand.NotifyCanExecuteChanged();
        AddAnimationCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>One of the clips the Clip Editor lists, with the frames, speed and looping it plays with.</summary>
/// <param name="HasOwnSpeed">True if the clip sets its own speed rather than using the animation's.</param>
/// <param name="HasOwnLoop">True if the clip says whether it loops rather than following the animation.</param>
/// <param name="IsOwnFrames">True for the animation's own frames (<see cref="ClipEditor.OwnFrames"/>), played without a clip.</param>
/// <param name="IsStart">True for what the game starts playing.</param>
public sealed record ClipRow(string Name, int First, int Count, float FramesPerSecond, bool Loop, bool HasOwnSpeed, bool HasOwnLoop,
    bool IsOwnFrames, bool IsStart)
{
    public string FramesText => Count <= 1 ? $"frame {First}" : $"frames {First}–{First + Count - 1}";

    public string SpeedText => FramesPerSecond.ToString("0.##", CultureInfo.InvariantCulture) + " fps";

    public string LoopText => Loop ? "loops" : "once";

    /// <summary>E.g. "frames 4–9 · 12 fps · loops".</summary>
    public string Summary => $"{FramesText} · {SpeedText} · {LoopText}";
}

/// <summary>One frame in the Clip Editor's strip: which of the clip's frames, which frame of the sheet, and its picture.</summary>
public sealed class ClipFrame(int index, int sheetFrame, ImageSource? image) : ObservableObject
{
    private bool _isCurrent;

    public int Index { get; } = index;

    public int SheetFrame { get; } = sheetFrame;

    public ImageSource? Image { get; } = image;

    /// <summary>True for the frame the preview is showing.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }
}
