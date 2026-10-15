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
using Griff.Modules.Fighting;
using Griff.Scenes;
using Animation = Griff.Scenes.Animation;
using Vector2 = System.Numerics.Vector2;

namespace Griff.Editor.Features.Art;

/// <summary>
/// The Clip Editor: the selected entity's animation clips, listed with their frames, speed and looping, and the one
/// picked playing in a preview the way the game plays it. Clips are added, renamed and removed here, one is picked for
/// the game to start with, and the one picked gets its frames from the sprite sheet shown beside it and its speed and
/// looping set, each as one undo step. Over the frame showing, its hitboxes and hurtboxes (a <see cref="Hitboxes"/>'s) are
/// drawn, moved and resized, copied from the frame before, and given their damage, knockback, stun, group or multiplier,
/// with the frame before shown faintly underneath (onion skin). It follows the selection, so it stays open beside the scene.
/// </summary>
public sealed class ClipEditor : ObservableObject
{
    /// <summary>What the list calls an animation's own frames (its First Frame and Frame Count), which it plays without a clip.</summary>
    public const string OwnFrames = "(own frames)";

    private const int MaxStrip = 100;

    private const int MaxSheet = 1024;

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
    private (int First, int Last)? _highlight;
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
        CopyPreviousFrameCommand = new RelayCommand(CopyPreviousFrame, () => CanEditBoxes && _frame > 0);
        RemoveBoxCommand = new RelayCommand(() => RemoveBox(), () => SelectedBox is not null);

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
            // While the list is made again, the list box lets go of what it showed picked: that isn't a pick.
            if (_rebuilding || !SetProperty(ref _selected, value))
                return;
            OnPropertyChanged(nameof(HasSelected));
            NotifySettings();
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

    /// <summary>
    /// Every frame of the sprite sheet, cut the way the sprite cuts it, in order, to pick the previewed clip's frames
    /// from. Those it plays are marked, and so is the one showing.
    /// </summary>
    public ObservableCollection<SheetCell> Sheet { get; } = [];

    /// <summary>How many frames across the sheet is (the sprite's Columns).</summary>
    public int SheetColumns => Sprite is { } sprite ? Math.Max(1, sprite.Columns) : 1;

    /// <summary>
    /// How many frames a second the previewed clip plays. Setting it gives the clip a speed of its own (or, for the
    /// animation's own frames, sets the animation's, which clips without their own use too), as one undo step.
    /// </summary>
    public float Speed
    {
        get => Selected?.FramesPerSecond ?? 0;
        set => SetSpeed(value);
    }

    /// <summary>Whether the previewed clip loops (or plays once and stops on its last frame). Setting it is one undo step.</summary>
    public bool Loops
    {
        get => Selected?.Loop ?? false;
        set => SetLoop(value);
    }

    /// <summary>Where the previewed clip's speed and looping come from, e.g. "Speed and looping from the animation".</summary>
    public string SettingsNote => Selected switch
    {
        null => "",
        { IsOwnFrames: true } => "Clips without a speed or looping of their own use these too.",
        { HasOwnSpeed: true, HasOwnLoop: true } => "",
        { HasOwnSpeed: false, HasOwnLoop: false } => "Speed and looping from the animation, until set here.",
        { HasOwnSpeed: false } => "Speed from the animation, until set here.",
        _ => "Looping from the animation, until set here.",
    };

    public IRelayCommand PlayCommand { get; }

    public IRelayCommand StepBackCommand { get; }

    public IRelayCommand StepForwardCommand { get; }

    public IRelayCommand AddClipCommand { get; }

    public IRelayCommand RenameClipCommand { get; }

    public IRelayCommand RemoveClipCommand { get; }

    public IRelayCommand StartWithClipCommand { get; }

    public IRelayCommand AddAnimationCommand { get; }

    public IRelayCommand<ClipFrame> ShowFrameCommand { get; }

    public IRelayCommand CopyPreviousFrameCommand { get; }

    public IRelayCommand RemoveBoxCommand { get; }

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
        Change($"Rename Clip {row.Name} to {name}", clips, start, BoxesEdit(LinesWith(box => Same(box.Clip, row.Name) ? box with { Clip = name } : box)));
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
        Change($"Remove Clip {row.Name}", clips, start, BoxesEdit(LinesWith(box => Same(box.Clip, row.Name) ? null : box)));
        Selected = Clips.Count == 0 ? null : Clips[Math.Clamp(index, 0, Clips.Count - 1)];
    }

    /// <summary>Makes the previewed clip the one the game starts with (the animation's Clip).</summary>
    public void StartWithClip()
    {
        if (Animation is not { } animation || Selected is not { } row)
            return;
        Change(row.IsOwnFrames ? "Start Without a Clip" : $"Start With {row.Name}", animation.ClipList, row.IsOwnFrames ? null : row.Name);
    }

    /// <summary>
    /// Makes the previewed clip play the sheet's frames <paramref name="first"/> to <paramref name="last"/> (either way
    /// round; the same for one frame), as one undo step. For the animation's own frames, sets its First Frame and Frame Count.
    /// </summary>
    public void SetFrames(int first, int last)
    {
        _highlight = null;
        if (Selected is not { } row)
            return;
        (first, last) = (Math.Min(first, last), Math.Max(first, last));
        if (first < 0)
            throw new InvalidOperationException("Sheet frames count from 0.");
        if (SheetFrames > 1 && last >= SheetFrames)
            throw new InvalidOperationException($"The sheet has {SheetFrames} frames, 0 to {SheetFrames - 1}.");
        var count = last - first + 1;
        if (first == row.First && count == row.Count)
        {
            MarkSheet();
            return;
        }

        var frames = count == 1 ? $"Frame {first}" : $"Frames {first}-{last}";
        if (row.IsOwnFrames)
            SetAnimation($"Set Frames to {frames}", (nameof(Griff.Scenes.Animation.FirstFrame), first), (nameof(Griff.Scenes.Animation.FrameCount), count));
        else
            ChangeClip($"Set {row.Name} to {frames}", clip => clip with { FirstFrame = first, FrameCount = count });
    }

    /// <summary>
    /// Shift-clicking the sheet: stretches the previewed clip to reach <paramref name="frame"/>, from its first frame
    /// (or, for a frame before it, from its last).
    /// </summary>
    public void ExtendFrames(int frame)
    {
        if (Selected is not { } row)
            return;
        SetFrames(frame < row.First ? row.First + row.Count - 1 : row.First, frame);
    }

    /// <summary>Marks the sheet's frames <paramref name="first"/> to <paramref name="last"/> while a drag picks them, without changing anything.</summary>
    public void HighlightFrames(int first, int last)
    {
        _highlight = (Math.Min(first, last), Math.Max(first, last));
        MarkSheet();
    }

    /// <summary>Stops marking frames being picked (a drag called off).</summary>
    public void ClearHighlight()
    {
        _highlight = null;
        MarkSheet();
    }

    /// <summary>Sets how many frames a second the previewed clip plays (see <see cref="Speed"/>), as one undo step.</summary>
    public void SetSpeed(float framesPerSecond)
    {
        if (Selected is not { } row)
            return;
        if (!float.IsFinite(framesPerSecond) || framesPerSecond < 0)
            throw new InvalidOperationException("A clip's speed is frames a second, 0 or more (0 holds its first frame).");
        framesPerSecond = MathF.Round(framesPerSecond, 3);
        var text = framesPerSecond.ToString("0.###", CultureInfo.InvariantCulture);
        if (row.IsOwnFrames)
        {
            if (framesPerSecond != row.FramesPerSecond)
                SetAnimation($"Set Speed to {text} fps", (nameof(Griff.Scenes.Animation.FramesPerSecond), framesPerSecond));
        }
        else if (!row.HasOwnSpeed || framesPerSecond != row.FramesPerSecond)
            ChangeClip($"Set {row.Name} Speed to {text} fps", clip => clip with { FramesPerSecond = framesPerSecond });
        NotifySettings();
    }

    /// <summary>Sets whether the previewed clip loops (see <see cref="Loops"/>), as one undo step.</summary>
    public void SetLoop(bool loop)
    {
        if (Selected is not { } row)
            return;
        if (row.IsOwnFrames)
        {
            if (loop != row.Loop)
                SetAnimation(loop ? "Loop" : "Play Once", (nameof(Griff.Scenes.Animation.Loop), loop));
        }
        else if (!row.HasOwnLoop || loop != row.Loop)
            ChangeClip(loop ? $"Loop {row.Name}" : $"Play {row.Name} Once", clip => clip with { Loop = loop });
        NotifySettings();
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

    // ----- Hitboxes and hurtboxes on the frame showing

    private (string Clip, int Frame)? _boxesFor;
    // True while the lists are made again (see Selected).
    private bool _rebuilding;
    private int _selectedBoxIndex = -1;
    private BoxTool _tool;
    private bool _drawCircles;
    private bool _onionSkin;
    private ImageSource? _onionImage;

    /// <summary>The entity's <see cref="Griff.Modules.Fighting.Hitboxes"/>, which holds its boxes (the first box drawn adds one).</summary>
    public Hitboxes? Hitboxes => _entity?.GetComponent<Hitboxes>();

    /// <summary>The boxes on the frame showing, hitboxes and hurtboxes, in the order they're listed.</summary>
    public ObservableCollection<FrameBoxRow> Boxes { get; } = [];

    /// <summary>The boxes on the frame before the one showing, drawn faintly while <see cref="OnionSkin"/> is on.</summary>
    public ObservableCollection<FrameBoxRow> OnionBoxes { get; } = [];

    /// <summary>True when the frame showing can have boxes: a clip picked, on an entity with a sprite and an animation.</summary>
    public bool CanEditBoxes => Selected is not null && Sprite is not null && Animation is not null;

    /// <summary>What the boxes' lines call the clip previewed: its name, or <see cref="FrameBox.OwnFrames"/> for the animation's own frames.</summary>
    public string BoxClip => Selected is null or { IsOwnFrames: true } ? FrameBox.OwnFrames : Selected.Name;

    /// <summary>How big the frame's picture is in the game, in world units (the sprite's Size): what the boxes are measured in.</summary>
    public Vector2 FrameSize => Sprite?.Size ?? Vector2.One;

    /// <summary>The box picked on the frame showing, to move, resize, set or remove; null for none.</summary>
    public FrameBoxRow? SelectedBox
    {
        get => _selectedBoxIndex >= 0 && _selectedBoxIndex < Boxes.Count ? Boxes[_selectedBoxIndex] : null;
        set
        {
            if (_rebuilding)
                return;
            var index = value is null ? -1 : Boxes.IndexOf(value);
            if (index == _selectedBoxIndex)
                return;
            _selectedBoxIndex = index;
            NotifyBox();
        }
    }

    /// <summary>What dragging over the frame does: pick and move boxes, or draw a new hitbox or hurtbox.</summary>
    public BoxTool Tool
    {
        get => _tool;
        set
        {
            if (!SetProperty(ref _tool, value))
                return;
            OnPropertyChanged(nameof(IsSelectTool));
            OnPropertyChanged(nameof(IsHitboxTool));
            OnPropertyChanged(nameof(IsHurtboxTool));
        }
    }

    public bool IsSelectTool
    {
        get => Tool == BoxTool.Select;
        set { if (value) Tool = BoxTool.Select; }
    }

    public bool IsHitboxTool
    {
        get => Tool == BoxTool.Hitbox;
        set { if (value) Tool = BoxTool.Hitbox; }
    }

    public bool IsHurtboxTool
    {
        get => Tool == BoxTool.Hurtbox;
        set { if (value) Tool = BoxTool.Hurtbox; }
    }

    /// <summary>Draw circles rather than boxes with the Hitbox and Hurtbox tools.</summary>
    public bool DrawCircles
    {
        get => _drawCircles;
        set => SetProperty(ref _drawCircles, value);
    }

    /// <summary>Shows the frame before the one showing faintly underneath it, with its boxes, to line the boxes up with.</summary>
    public bool OnionSkin
    {
        get => _onionSkin;
        set
        {
            if (!SetProperty(ref _onionSkin, value))
                return;
            _boxesFor = null;
            RefreshBoxes();
        }
    }

    /// <summary>The frame before the one showing, while <see cref="OnionSkin"/> is on (and there is one).</summary>
    public ImageSource? OnionImage
    {
        get => _onionImage;
        private set => SetProperty(ref _onionImage, value);
    }

    /// <summary>What the frame showing has, e.g. "Frame 2: 1 hitbox, 2 hurtboxes".</summary>
    public string BoxesSummary
    {
        get
        {
            if (!CanEditBoxes)
                return "";
            var hits = Boxes.Count(b => b.IsHit);
            var hurts = Boxes.Count - hits;
            if (hits + hurts == 0)
                return $"Frame {_frame + 1}: no boxes. Pick Hitbox or Hurtbox and drag over the picture.";
            var parts = new List<string>();
            if (hits > 0)
                parts.Add(hits == 1 ? "1 hitbox" : $"{hits} hitboxes");
            if (hurts > 0)
                parts.Add(hurts == 1 ? "1 hurtbox" : $"{hurts} hurtboxes");
            return $"Frame {_frame + 1}: {string.Join(", ", parts)}";
        }
    }

    public bool HasSelectedBox => SelectedBox is not null;

    public bool IsHitboxSelected => SelectedBox is { IsHit: true };

    public bool IsHurtboxSelected => SelectedBox is { IsHit: false };

    /// <summary>The picked hitbox's damage (before the hurtbox it lands on multiplies it). Setting it is one undo step.</summary>
    public float BoxDamage
    {
        get => SelectedBox?.Box.Damage ?? 0;
        set => ChangeSelectedBox("Damage", Text(value), box => box with { Damage = NotNegative(value, "Damage") });
    }

    /// <summary>How fast the picked hitbox sends what it hits away from it, in units a second. One undo step.</summary>
    public float BoxKnockbackX
    {
        get => SelectedBox?.Box.Knockback.X ?? 0;
        set => ChangeSelectedBox("Knockback", Text(value), box => box with { Knockback = box.Knockback with { X = Finite(value) } });
    }

    /// <summary>How fast the picked hitbox sends what it hits upward, in units a second. One undo step.</summary>
    public float BoxKnockbackY
    {
        get => SelectedBox?.Box.Knockback.Y ?? 0;
        set => ChangeSelectedBox("Knockback", Text(value), box => box with { Knockback = box.Knockback with { Y = Finite(value) } });
    }

    /// <summary>How many steps (frames, at 60 a second) of hitstun the picked hitbox leaves what it hits in. One undo step.</summary>
    public int BoxStun
    {
        get => SelectedBox?.Box.Stun ?? 0;
        set => ChangeSelectedBox("Stun", value.ToString(CultureInfo.InvariantCulture), box => box with { Stun = (int)NotNegative(value, "Stun") });
    }

    /// <summary>The picked hitbox's group: a swing hits each thing once per group. One undo step.</summary>
    public int BoxGroup
    {
        get => SelectedBox?.Box.Group ?? 1;
        set => ChangeSelectedBox("Group", value.ToString(CultureInfo.InvariantCulture), box => box with { Group = value });
    }

    /// <summary>What the picked hurtbox multiplies the damage of hits by: 1.5 for a head that takes more. One undo step.</summary>
    public float BoxMultiplier
    {
        get => SelectedBox?.Box.Multiplier ?? 1;
        set => ChangeSelectedBox("Multiplier", Text(value), box => box with { Multiplier = NotNegative(value, "A multiplier") });
    }

    /// <summary>Undoes the scene's last change, as Edit &gt; Undo does (Ctrl+Z in the Clip Editor).</summary>
    public void Undo()
    {
        if (_document?.History.CanUndo == true)
            _document.History.Undo();
    }

    /// <summary>Redoes the last change undone, as Edit &gt; Redo does (Ctrl+Y in the Clip Editor).</summary>
    public void Redo()
    {
        if (_document?.History.CanRedo == true)
            _document.History.Redo();
    }

    /// <summary>Picks the box at <paramref name="index"/> of <see cref="Boxes"/> (-1 for none).</summary>
    public void SelectBox(int index)
    {
        if (index >= Boxes.Count)
            throw new InvalidOperationException($"Frame {_frame + 1} has {Boxes.Count} boxes, 0 to {Boxes.Count - 1}.");
        SelectedBox = index < 0 ? null : Boxes[index];
    }

    /// <summary>
    /// Adds a hitbox or hurtbox to the frame showing, its middle at <paramref name="x"/>, <paramref name="y"/> and
    /// <paramref name="width"/> by <paramref name="height"/> big, in world units from the entity's middle facing right, and
    /// picks it. A hitbox starts with 1 damage in group 1. One undo step (adding a Hitboxes if the entity has none).
    /// </summary>
    public void AddBox(FrameBoxKind kind, float x, float y, float width, float height)
    {
        var size = new Vector2(Positive(width, "A box's width"), Positive(height, "A box's height"));
        Add(new FrameBox(BoxClip, _frame, kind, new Vector2(Finite(x), Finite(y)), Rounded(size)));
    }

    /// <summary>Adds a circle to the frame showing, its middle at <paramref name="x"/>, <paramref name="y"/>, as <see cref="AddBox"/> does a box.</summary>
    public void AddCircle(FrameBoxKind kind, float x, float y, float radius) =>
        Add(new FrameBox(BoxClip, _frame, kind, new Vector2(Finite(x), Finite(y)), Vector2.One).AsCircle(MathF.Round(Positive(radius, "A circle's radius"), 3)));

    /// <summary>Moves the box at <paramref name="index"/> so its middle is at <paramref name="x"/>, <paramref name="y"/>. One undo step.</summary>
    public void MoveBox(int index, float x, float y) =>
        ChangeBox(index, "Move", box => box with { Center = Rounded(new Vector2(Finite(x), Finite(y))) });

    /// <summary>Makes the box at <paramref name="index"/> <paramref name="width"/> by <paramref name="height"/> (a circle that wide), keeping its middle. One undo step.</summary>
    public void ResizeBox(int index, float width, float height)
    {
        var size = new Vector2(Positive(width, "A box's width"), Positive(height, "A box's height"));
        ChangeBox(index, "Resize", box => box.IsCircle ? box.AsCircle(MathF.Round(MathF.Max(size.X, size.Y) / 2, 3)) : box with { Size = Rounded(size) });
    }

    /// <summary>Puts the box at <paramref name="index"/> at <paramref name="x"/>, <paramref name="y"/> and makes it <paramref name="width"/> by <paramref name="height"/>: dragging a corner. One undo step.</summary>
    public void SetBoxBounds(int index, float x, float y, float width, float height)
    {
        var size = new Vector2(Positive(width, "A box's width"), Positive(height, "A box's height"));
        var center = Rounded(new Vector2(Finite(x), Finite(y)));
        ChangeBox(index, "Resize", box => box.IsCircle
            ? box.AsCircle(MathF.Round(MathF.Max(size.X, size.Y) / 2, 3)) with { Center = center }
            : box with { Center = center, Size = Rounded(size) });
    }

    /// <summary>Removes the box at <paramref name="index"/> (the one picked, without one). One undo step.</summary>
    public void RemoveBox(int index = -1)
    {
        index = index < 0 ? _selectedBoxIndex : index;
        if (index < 0)
            return;
        var row = Row(index);
        _selectedBoxIndex = -1;
        ChangeFrame($"Remove {row.Kind} from {FrameName}", boxes =>
        {
            boxes.RemoveAt(index);
            return true;
        });
    }

    /// <summary>Gives the frame showing the boxes of the frame before it, in place of its own: a punch's fist moving on a little. One undo step.</summary>
    public void CopyPreviousFrame()
    {
        if (!CanEditBoxes)
            return;
        if (_frame == 0)
            throw new InvalidOperationException("The first frame has no frame before it to copy from.");
        var previous = Hitboxes is { } hitboxes ? hitboxes.On(BoxClip, _frame - 1).ToArray() : [];
        ChangeFrame($"Copy Boxes from Frame {_frame} to {FrameName}", boxes =>
        {
            var copied = previous.Select(b => b with { Frame = _frame }).ToList();
            if (copied.SequenceEqual(boxes))
                return false;
            boxes.Clear();
            boxes.AddRange(copied);
            _selectedBoxIndex = -1;
            return true;
        });
    }

    // E.g. "Attack Frame 2".
    private string FrameName => $"{(Selected is { IsOwnFrames: false } clip ? clip.Name : "Frame")}{(Selected is { IsOwnFrames: false } ? " Frame" : "")} {_frame + 1}";

    private void Add(FrameBox box)
    {
        ChangeFrame($"Add {(box.IsHit ? "Hitbox" : "Hurtbox")} to {FrameName}", boxes =>
        {
            boxes.Add(box);
            _selectedBoxIndex = boxes.Count - 1;
            return true;
        });
    }

    private FrameBoxRow Row(int index) =>
        index >= 0 && index < Boxes.Count ? Boxes[index] : throw new InvalidOperationException(Boxes.Count == 0
            ? $"Frame {_frame + 1} has no boxes."
            : $"Frame {_frame + 1} has {Boxes.Count} boxes, 0 to {Boxes.Count - 1}.");

    private void ChangeBox(int index, string verb, Func<FrameBox, FrameBox> change) =>
        ChangeBox(index, row => $"{verb} {row.Kind} on {FrameName}", change);

    private void ChangeBox(int index, Func<FrameBoxRow, string> describe, Func<FrameBox, FrameBox> change)
    {
        var row = Row(index);
        _selectedBoxIndex = index;
        ChangeFrame(describe(row), boxes =>
        {
            var changed = change(boxes[index]);
            if (changed == boxes[index])
                return false;
            boxes[index] = changed;
            return true;
        });
    }

    // E.g. "Set Hitbox Damage to 3".
    private void ChangeSelectedBox(string what, string value, Func<FrameBox, FrameBox> change)
    {
        if (SelectedBox is not { } row)
            return;
        ChangeBox(row.Index, _ => $"Set {row.Kind} {what} to {value}", change);
        NotifyBox();
    }

    // Changes the frame showing's boxes as one undo step, pausing on it; nothing happens if the change says nothing changed.
    private void ChangeFrame(string description, Func<List<FrameBox>, bool> change)
    {
        if (!CanEditBoxes || _entity is not { } entity || _document is not { } document)
            return;
        if (IsPlaying)
            ShowFrame(_frame);
        var (clip, frame) = (BoxClip, _frame);
        var mine = Hitboxes is { } hitboxes ? hitboxes.On(clip, frame).ToArray().ToList() : [];
        var before = mine.ToList();
        if (!change(mine) || mine.SequenceEqual(before))
        {
            NotifyBox();
            return;
        }

        var lines = ReplaceFrame(clip, frame, mine);
        IEdit edit = Hitboxes is { } existing
            ? BoxesEdit(existing, lines)
            : new AddComponentEdit(entity, new Hitboxes { Boxes = lines });
        document.History.Seal();
        document.Execute(new CompositeEdit(description, [edit]));
        document.History.Seal();
    }

    // The boxes' lines with the frame's own replaced by these, where its first one was (or at the end); the rest as they were.
    private List<string> ReplaceFrame(string clip, int frame, IReadOnlyList<FrameBox> boxes)
    {
        var lines = new List<string>();
        var at = -1;
        foreach (var line in Hitboxes?.Boxes ?? [])
        {
            if (FrameBox.TryParse(line, out var box, out _) && box.Frame == frame && Same(box.Clip, clip))
            {
                if (at < 0)
                    at = lines.Count;
                continue;
            }

            lines.Add(line);
        }

        lines.InsertRange(at < 0 ? lines.Count : at, boxes.Select(b => b.ToString()));
        return lines;
    }

    // The boxes' lines with each box changed (or dropped, for null); lines that don't read, and boxes left as they were, kept as written.
    private List<string>? LinesWith(Func<FrameBox, FrameBox?> change)
    {
        if (Hitboxes is not { } hitboxes)
            return null;
        var lines = new List<string>();
        foreach (var line in hitboxes.Boxes)
        {
            if (!FrameBox.TryParse(line, out var box, out _))
            {
                lines.Add(line);
                continue;
            }

            if (change(box) is { } changed)
                lines.Add(changed == box ? line : changed.ToString());
        }

        return lines;
    }

    private SetPropertyEdit? BoxesEdit(IReadOnlyList<string>? lines) =>
        lines is null || Hitboxes is not { } hitboxes || lines.SequenceEqual(hitboxes.Boxes) ? null : BoxesEdit(hitboxes, lines);

    private SetPropertyEdit BoxesEdit(Hitboxes hitboxes, IReadOnlyList<string> lines) =>
        new(hitboxes, _workspace.Components.Get(typeof(Hitboxes)).Properties.Single(p => p.Name == nameof(Griff.Modules.Fighting.Hitboxes.Boxes)), lines);

    // Lists the boxes of the frame showing (and the frame before, for the onion skin) when the frame changes or they do.
    private void RefreshBoxes()
    {
        (string, int)? key = CanEditBoxes ? (BoxClip, _frame) : null;
        if (key == _boxesFor)
            return;
        _boxesFor = key;
        var rebuilding = _rebuilding;
        _rebuilding = true;
        Boxes.Clear();
        OnionBoxes.Clear();
        if (key is var (clip, frame) && Hitboxes is { } hitboxes)
        {
            Fill(Boxes, hitboxes.On(clip, frame));
            if (OnionSkin && frame > 0)
                Fill(OnionBoxes, hitboxes.On(clip, frame - 1));
        }

        OnionImage = OnionSkin && key is not null && _frame > 0 && Selected is { } row ? SheetImage(row.First + _frame - 1) : null;
        _rebuilding = rebuilding;
        if (_selectedBoxIndex >= Boxes.Count)
            _selectedBoxIndex = -1;
        OnPropertyChanged(nameof(Hitboxes));
        OnPropertyChanged(nameof(CanEditBoxes));
        OnPropertyChanged(nameof(BoxClip));
        OnPropertyChanged(nameof(FrameSize));
        NotifyBox();
        CopyPreviousFrameCommand.NotifyCanExecuteChanged();

        static void Fill(ObservableCollection<FrameBoxRow> rows, ReadOnlySpan<FrameBox> boxes)
        {
            var (hits, hurts) = (0, 0);
            for (var i = 0; i < boxes.Length; i++)
                rows.Add(new FrameBoxRow(i, boxes[i], boxes[i].IsHit ? ++hits : ++hurts));
        }
    }

    private void NotifyBox()
    {
        OnPropertyChanged(nameof(SelectedBox));
        OnPropertyChanged(nameof(HasSelectedBox));
        OnPropertyChanged(nameof(IsHitboxSelected));
        OnPropertyChanged(nameof(IsHurtboxSelected));
        OnPropertyChanged(nameof(BoxDamage));
        OnPropertyChanged(nameof(BoxKnockbackX));
        OnPropertyChanged(nameof(BoxKnockbackY));
        OnPropertyChanged(nameof(BoxStun));
        OnPropertyChanged(nameof(BoxGroup));
        OnPropertyChanged(nameof(BoxMultiplier));
        OnPropertyChanged(nameof(BoxesSummary));
        RemoveBoxCommand.NotifyCanExecuteChanged();
    }

    private static string Text(float value) => FrameBox.Number(value);

    private static float Finite(float value) =>
        float.IsFinite(value) ? MathF.Round(value, 3) : throw new InvalidOperationException("That isn't a number.");

    private static float Positive(float value, string what) =>
        float.IsFinite(value) && value > 0 ? value : throw new InvalidOperationException($"{what} is more than 0.");

    private static float NotNegative(float value, string what) =>
        float.IsFinite(value) && value >= 0 ? MathF.Round(value, 3) : throw new InvalidOperationException($"{what} is 0 or more.");

    private static Vector2 Rounded(Vector2 value) => new(MathF.Round(value.X, 3), MathF.Round(value.Y, 3));

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
        // The boxes are listed again too, whatever frame shows (they may have changed).
        _boxesFor = null;
        if (pickStart)
            _selectedBoxIndex = -1;
        _rebuilding = true;
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
        _highlight = null;
        Notice = NoticeFor(animation);
        Sheet.Clear();
        if (animation is not null && Sprite is not null)
        {
            for (var i = 0; i < Math.Min(SheetFrames, MaxSheet); i++)
                Sheet.Add(new SheetCell(i, SheetImage(i)));
        }

        _selected = (keep is null ? null : Find(keep)) ?? Clips.FirstOrDefault(c => c.IsStart) ?? Clips.FirstOrDefault();
        _rebuilding = false;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelected));
        OnPropertyChanged(nameof(EntityName));
        OnPropertyChanged(nameof(Animation));
        OnPropertyChanged(nameof(Sprite));
        OnPropertyChanged(nameof(HasAnimation));
        OnPropertyChanged(nameof(CanAddAnimation));
        OnPropertyChanged(nameof(SheetColumns));
        NotifySettings();
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

        MarkSheet();
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
        var showing = Selected is null ? -1 : SheetFrame;
        foreach (var cell in Sheet)
            cell.IsCurrent = cell.Index == showing;
        OnPropertyChanged(nameof(Time));
        OnPropertyChanged(nameof(Frame));
        OnPropertyChanged(nameof(SheetFrame));
        OnPropertyChanged(nameof(FrameText));
        RefreshBoxes();
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

    // Marks the sheet's frames the previewed clip plays, and those a drag is picking.
    private void MarkSheet()
    {
        var (first, last) = Selected is { } clip ? (clip.First, clip.First + clip.Count - 1) : (-1, -2);
        foreach (var cell in Sheet)
        {
            // While a drag picks frames, only those it's picking are marked.
            cell.InClip = _highlight is null && cell.Index >= first && cell.Index <= last;
            cell.IsPicking = _highlight is var (from, to) && cell.Index >= from && cell.Index <= to;
        }
    }

    // ----- Changing the clips

    // Sets the animation's Clips (and Clip, the one it starts with) as one undo step.
    private void Change(string description, IReadOnlyList<AnimationClip> clips, string? start, IEdit? boxes = null)
    {
        if (Animation is not { } animation)
            return;
        var text = clips.Count == 0 ? null : Griff.Scenes.Animation.FormatClips(clips);
        var changes = new List<(string, object?)>();
        if (text != animation.Clips)
            changes.Add((nameof(Griff.Scenes.Animation.Clips), text));
        if (!string.Equals(start ?? "", animation.Clip ?? "", StringComparison.Ordinal))
            changes.Add((nameof(Griff.Scenes.Animation.Clip), start));
        SetAnimation(description, boxes, [.. changes]);
    }

    // Changes the previewed clip in the animation's Clips, as one undo step.
    private void ChangeClip(string description, Func<AnimationClip, AnimationClip> change)
    {
        if (Animation is not { } animation || Selected is not { IsOwnFrames: false } row)
            return;
        Change(description, animation.ClipList.Select(c => Same(c.Name, row.Name) ? change(c) : c).ToList(), animation.Clip);
    }

    // Sets properties of the animation, as one undo step.
    private void SetAnimation(string description, params (string Property, object? Value)[] changes) => SetAnimation(description, null, changes);

    // ...with a change to the boxes that goes with it (a clip renamed or removed takes its boxes along).
    private void SetAnimation(string description, IEdit? boxes, params (string Property, object? Value)[] changes)
    {
        if (Animation is not { } animation || _document is not { } document || changes.Length == 0)
            return;
        var properties = _workspace.Components.Get(typeof(Animation)).Properties;
        var edits = changes.Select(c => (IEdit)new SetPropertyEdit(animation, properties.Single(p => p.Name == c.Property), c.Value)).ToList();
        if (boxes is not null)
            edits.Add(boxes);
        document.History.Seal();
        document.Execute(new CompositeEdit(description, edits));
        document.History.Seal();
    }

    private void NotifySettings()
    {
        OnPropertyChanged(nameof(Speed));
        OnPropertyChanged(nameof(Loops));
        OnPropertyChanged(nameof(SettingsNote));
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

    /// <summary>What agents see it as, e.g. "Sheet frame 5, showing".</summary>
    public string Label => $"Sheet frame {SheetFrame}{(IsCurrent ? ", showing" : "")}";

    /// <summary>True for the frame the preview is showing.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }
}

/// <summary>
/// One frame of the sprite sheet in the Clip Editor, to pick the clip's frames from: marked when the clip plays it,
/// while a drag is picking it, and while the preview shows it.
/// </summary>
public sealed class SheetCell(int index, ImageSource? image) : ObservableObject
{
    private bool _inClip;
    private bool _isPicking;
    private bool _isCurrent;

    /// <summary>Which frame of the sheet, counting from 0 (what a clip's frames are numbered by).</summary>
    public int Index { get; } = index;

    public ImageSource? Image { get; } = image;

    /// <summary>What agents see it as, e.g. "Frame 3, in the clip, showing".</summary>
    public string Label => $"Frame {Index}{(InClip ? ", in the clip" : "")}{(IsPicking ? ", being picked" : "")}{(IsCurrent ? ", showing" : "")}";

    /// <summary>True when the clip picked plays this frame.</summary>
    public bool InClip
    {
        get => _inClip;
        set => SetProperty(ref _inClip, value);
    }

    /// <summary>True while a drag across the sheet is picking this frame.</summary>
    public bool IsPicking
    {
        get => _isPicking;
        set => SetProperty(ref _isPicking, value);
    }

    /// <summary>True for the frame the preview is showing.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }
}

/// <summary>What dragging over the Clip Editor's frame does.</summary>
public enum BoxTool
{
    /// <summary>Picks a box, moves it, and resizes it by its corners.</summary>
    Select,

    /// <summary>Draws a new hitbox.</summary>
    Hitbox,

    /// <summary>Draws a new hurtbox.</summary>
    Hurtbox,
}

/// <summary>One of the boxes on the frame the Clip Editor shows: a hitbox or a hurtbox, where it is and what it does.</summary>
/// <param name="Index">Where it is among the frame's boxes, counting from 0 (what the Clip Editor's box methods take).</param>
/// <param name="Number">Which hitbox (or hurtbox) of the frame it is, counting from 1.</param>
public sealed record FrameBoxRow(int Index, FrameBox Box, int Number)
{
    public bool IsHit => Box.IsHit;

    /// <summary>"Hitbox" or "Hurtbox".</summary>
    public string Kind => IsHit ? "Hitbox" : "Hurtbox";

    /// <summary>What agents and the list see it as, e.g. "Hitbox 1: 2 damage, knockback 4, 3, 18 stun" or "Hurtbox 2 (circle), takes ×1.5".</summary>
    public string Label
    {
        get
        {
            var text = $"{Kind} {Number}{(Box.IsCircle ? " (circle)" : "")}";
            if (!IsHit)
                return Box.Multiplier == 1 ? text : $"{text}, takes ×{FrameBox.Number(Box.Multiplier)}";
            var parts = new List<string> { $"{FrameBox.Number(Box.Damage)} damage" };
            if (Box.Knockback != System.Numerics.Vector2.Zero)
                parts.Add($"knockback {FrameBox.Number(Box.Knockback.X)}, {FrameBox.Number(Box.Knockback.Y)}");
            if (Box.Stun > 0)
                parts.Add($"{Box.Stun} stun");
            if (Box.Group != 1)
                parts.Add($"group {Box.Group}");
            return $"{text}: {string.Join(", ", parts)}";
        }
    }

    /// <summary>Where and how big it is, e.g. "at 0.5, 0.2, 0.3 × 0.3" (world units from the entity's middle, facing right).</summary>
    public string Place => Box.IsCircle
        ? $"at {FrameBox.Number(Box.Center.X)}, {FrameBox.Number(Box.Center.Y)}, radius {FrameBox.Number(Box.Radius)}"
        : $"at {FrameBox.Number(Box.Center.X)}, {FrameBox.Number(Box.Center.Y)}, {FrameBox.Number(Box.Size.X)} × {FrameBox.Number(Box.Size.Y)}";
}
