using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Art;
using Griff.Modules.VisualNovels;
using Griff.Scenes;

namespace Griff.Editor.Features.Story;

/// <summary>
/// The Dialogue Editor: the selected entity's dialogue, a line to a row, and for the line picked who says it and with
/// what expression, what's said, and its stage directions, each picked from lists of what the stage has (its characters,
/// positions, expressions and backgrounds) rather than typed; beside them a preview of the stage as that line leaves it,
/// the speaker lit and the others dimmed. Every change rewrites the dialogue's Lines as one undo step, so it's the same
/// as typing them. It follows the selection, so it stays open beside the scene. Agents work it the same way: select a
/// line (<see cref="SelectLine"/>), set <see cref="Speaker"/>, <see cref="Expression"/> and <see cref="Said"/>, add
/// directions (<see cref="AddDirection"/>) and read <see cref="PreviewText"/>. For any dialogue, a stage or not, the line
/// picked's flags and messages are listed and added from what the project has (<see cref="AddTag"/>), a choice's or
/// go-to's <see cref="GoesTo"/> is picked from the parts, and choices, parts and go-tos are added (<see cref="AddChoice"/>,
/// <see cref="AddPart"/>, <see cref="AddGoTo"/>).
/// </summary>
public sealed partial class DialogueEditor : ObservableObject
{
    /// <summary>What the speaker list calls nobody: the line is said as it is (a narrator, a sign).</summary>
    public const string Nobody = "(nobody)";

    /// <summary>What the expression and transition lists call leaving it as it is.</summary>
    public const string AsIs = "(as is)";

    // Pixels a world unit is drawn in the preview, before it's scaled to fit.
    private const double PreviewScale = 40;

    private readonly IProjectSession _projectSession;
    private readonly ISelection _selection;
    private readonly EditorArt _art;
    private SceneDocument? _document;
    private Entity? _entity;
    private int _selectedIndex = -1;
    private DialogueEditorWindow? _window;
    private string _newKind = "Show";
    private string? _newWho;
    private string? _newPosition;
    private string? _newExpression;
    private string? _newBackground;
    private string _newTransition = AsIs;
    private bool _rebuilding;

    private readonly Core.EditorWindow _editorWindow;
    public DialogueEditor(ISceneDocuments sceneDocuments, IProjectSession projectSession, ISelection selection, EditorArt art, Core.EditorWindow editorWindow)
    {
        _projectSession = projectSession;
        _editorWindow = editorWindow;
        _selection = selection;
        _art = art;
        AddLineCommand = new RelayCommand(() => AddLine(), () => Dialogue is not null);
        RemoveLineCommand = new RelayCommand(() => RemoveLine(SelectedIndex), () => HasLine);
        MoveUpCommand = new RelayCommand(() => MoveLine(SelectedIndex, -1), () => HasLine && SelectedIndex > 0);
        MoveDownCommand = new RelayCommand(() => MoveLine(SelectedIndex, 1), () => HasLine && SelectedIndex < Lines.Count - 1);
        AddDirectionCommand = new RelayCommand(() => AddDirection(NewKind, NewWho, NewPosition, NewExpression, NewBackground, NewTransition), () => HasLine);
        RemoveDirectionCommand = new RelayCommand<DirectionRow>(row => { if (row is not null) RemoveDirection(row.Index); });

        selection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ISelection.Entity))
                Follow();
        };
        sceneDocuments.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ISceneDocuments.Scene))
                Track(sceneDocuments.Scene);
        };
        art.ImageSaved += (_, _) => RefreshPreview();
        Track(sceneDocuments.Scene);
    }

    public string Title => "Dialogue Editor";

    /// <summary>The dialogue being edited: the selected entity's.</summary>
    public Dialogue? Dialogue => _entity?.GetComponent<Dialogue>();

    public string EntityName => _entity?.Name ?? "";

    public bool HasDialogue => Dialogue is not null;

    /// <summary>The stage the dialogue directs: the one it's inside, or the scene's first; null with none.</summary>
    public Stage? Stage => _entity is null ? null
        : Stage.StageOf(_entity) ?? _document?.Scene.Entities.Select(e => e.GetComponent<Stage>()).OfType<Stage>().FirstOrDefault();

    public bool HasStage => Stage is not null;

    /// <summary>Why there's nothing (or not everything) to show, or null.</summary>
    public string? Notice => _entity is null ? "Select an entity with a Dialogue to edit its lines."
        : Dialogue is null ? $"{_entity.Name} has no Dialogue. Add one, or select an entity that has one (Entity > Genres > Visual Novels > Stage makes a stage with one)."
        : Stage is null ? "There's no stage in the scene, so lines can't show characters or backgrounds yet: Entity > Genres > Visual Novels > Stage adds one."
        : null;

    public bool HasNotice => Notice is not null;

    /// <summary>The dialogue's lines, a row each.</summary>
    public ObservableCollection<LineRow> Lines { get; } = [];

    /// <summary>The line picked, from 0; -1 for none.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            // The list box lets go of its pick while the lines are made again: that isn't a pick.
            if (_rebuilding)
                return;
            value = Lines.Count == 0 ? -1 : Math.Clamp(value, -1, Lines.Count - 1);
            if (SetProperty(ref _selectedIndex, value))
                NotifyLine();
        }
    }

    public bool HasLine => SelectedIndex >= 0 && SelectedIndex < Lines.Count;

    private StageLines.LineParts? Parts => HasLine ? StageLines.Split(Dialogue!.Lines[SelectedIndex]) : null;

    /// <summary>True when the line picked is one a speaker can say (not "# Part", "-&gt; End" or a choice).</summary>
    public bool CanHaveSpeaker => Parts is { IsLine: true, Lead.Length: 0 };

    /// <summary>The line picked as it's written. Setting it rewrites the row, as one undo step.</summary>
    public string RowText
    {
        get => HasLine ? Dialogue!.Lines[SelectedIndex] : "";
        set
        {
            if (HasLine)
                SetLine(SelectedIndex, value);
        }
    }

    /// <summary>Who says the line picked: a speaker's name, or <see cref="Nobody"/>.</summary>
    public string Speaker
    {
        get => Parts?.Speaker is { Length: > 0 } who ? AsListed(who, Speakers) : Nobody;
        set => Change(p => p with { Speaker = value is null or Nobody or "" ? null : value, Expression = value is null or Nobody or "" ? null : p.Expression });
    }

    /// <summary>The speakers to pick from: <see cref="Nobody"/>, then everyone with a Speaker in the scene.</summary>
    public IReadOnlyList<string> Speakers =>
        Kept([Nobody, .. (_document?.Scene.All<Speaker>().Select(s => s.ShownName) ?? []).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)]);

    /// <summary>The expression the line is said with ("Ava (happy): ..."), or <see cref="AsIs"/>.</summary>
    public string Expression
    {
        get => Parts?.Expression is { Length: > 0 } e ? AsListed(e, Expressions) : AsIs;
        set => Change(p => p with { Expression = value is null or AsIs or "" ? null : value });
    }

    /// <summary>The expressions the line's speaker has, to pick from (<see cref="AsIs"/> first).</summary>
    public IReadOnlyList<string> Expressions => Kept([AsIs, .. ExpressionsOf(Parts?.Speaker)]);

    /// <summary>What the line picked says (without its tags and speaker).</summary>
    public string Said
    {
        get => Parts?.Text ?? "";
        set => Change(p => p with { Text = value ?? "" });
    }

    /// <summary>The line picked's stage directions, in order.</summary>
    public ObservableCollection<DirectionRow> Directions { get; } = [];

    // ----- A new direction, built from lists -----

    /// <summary>The kinds of direction: Show, Hide, Move and Background.</summary>
    public IReadOnlyList<string> Kinds { get; } = Enum.GetNames<DirectionKind>();

    /// <summary>What the next direction added does: Show, Hide, Move or Background.</summary>
    public string NewKind
    {
        get => _newKind;
        set
        {
            if (SetProperty(ref _newKind, Enum.TryParse<DirectionKind>(value, true, out var kind) ? kind.ToString() : "Show"))
                OnPropertyChanged(nameof(IsNewBackground));
        }
    }

    /// <summary>True when the next direction changes the background (so it names a background, not a character).</summary>
    public bool IsNewBackground => NewKind == nameof(DirectionKind.Background);

    /// <summary>Who the next direction is for: one of <see cref="Characters"/>.</summary>
    public string? NewWho
    {
        get => _newWho;
        set
        {
            if (SetProperty(ref _newWho, value))
                OnPropertyChanged(nameof(NewExpressions));
        }
    }

    /// <summary>Where the next direction puts them: one of <see cref="Positions"/> (empty leaves it).</summary>
    public string? NewPosition
    {
        get => _newPosition;
        set => SetProperty(ref _newPosition, value);
    }

    /// <summary>How the next direction has them look: one of <see cref="NewExpressions"/> (empty leaves it).</summary>
    public string? NewExpression
    {
        get => _newExpression;
        set => SetProperty(ref _newExpression, value);
    }

    /// <summary>The background the next direction changes to: one of <see cref="Backgrounds"/>.</summary>
    public string? NewBackground
    {
        get => _newBackground;
        set => SetProperty(ref _newBackground, value);
    }

    /// <summary>How the next direction happens: <see cref="AsIs"/> (the stage's own), cut, fade or slide.</summary>
    public string NewTransition
    {
        get => _newTransition;
        set => SetProperty(ref _newTransition, string.IsNullOrWhiteSpace(value) ? AsIs : value);
    }

    /// <summary>The stage's characters, everyone and "all".</summary>
    public IReadOnlyList<string> Characters => Kept([.. Stage?.Cast.Select(c => c.Name) ?? [], "all"]);

    /// <summary>The stage's positions (empty first, to leave it).</summary>
    public IReadOnlyList<string> Positions => Kept(["", .. StageLines.Names(Stage?.Positions)]);

    /// <summary>The stage's backgrounds.</summary>
    public IReadOnlyList<string> Backgrounds => Kept(StageLines.Names(Stage?.Backgrounds));

    /// <summary>The expressions of the character the next direction is for (empty first, to leave it).</summary>
    public IReadOnlyList<string> NewExpressions => Kept(["", .. ExpressionsOf(NewWho)]);

    // The lists keep the same instance while what's in them doesn't change, so a list box bound to one keeps its pick.
    private readonly Dictionary<string, IReadOnlyList<string>> _lists = [];

    private IReadOnlyList<string> Kept(IReadOnlyList<string> now, [System.Runtime.CompilerServices.CallerMemberName] string list = "")
    {
        if (_lists.TryGetValue(list, out var before) && before.SequenceEqual(now))
            return before;
        _lists[list] = now;
        return now;
    }

    // A name as one of `names` spells it, whatever its capitals ("happy" is the character's "Happy").
    private static string AsListed(string name, IReadOnlyList<string> names) =>
        names.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ?? name;

    /// <summary>How directions can happen: <see cref="AsIs"/>, cut, fade and slide.</summary>
    public IReadOnlyList<string> Transitions { get; } = [AsIs, .. StageLines.TransitionWords];

    // ----- The preview -----

    /// <summary>The stage at the line picked, in a sentence: the background, who's on where looking how, who's speaking and what's said.</summary>
    public string PreviewText => Stage is { } stage && Dialogue is { } dialogue && HasLine ? StageLines.At(stage, dialogue, SelectedIndex).Describe() : "";

    /// <summary>Directions on the line picked that name something the stage doesn't have.</summary>
    public string Problems => Stage is { } stage && Dialogue is { } dialogue && HasLine ? string.Join("\n", StageLines.At(stage, dialogue, SelectedIndex).Problems) : "";

    public bool HasProblems => Problems.Length > 0;

    /// <summary>How wide and high the preview is drawn, in pixels before it's scaled to fit.</summary>
    public double PreviewWidth => Math.Max(1, Math.Abs(Stage?.Size.X ?? 16)) * PreviewScale;

    public double PreviewHeight => Math.Max(1, Math.Abs(Stage?.Size.Y ?? 9)) * PreviewScale;

    /// <summary>The background at the line picked: its picture, or its colors.</summary>
    public Brush? PreviewBackground { get; private set; }

    /// <summary>Those on stage at the line picked, where they stand and how they look.</summary>
    public ObservableCollection<PreviewFigure> PreviewFigures { get; } = [];

    /// <summary>Who's speaking at the line picked, for the name tag; empty for nobody.</summary>
    public string PreviewSpeaker { get; private set; } = "";

    /// <summary>What's said at the line picked.</summary>
    public string PreviewSaid { get; private set; } = "";

    public IRelayCommand AddLineCommand { get; }

    public IRelayCommand RemoveLineCommand { get; }

    public IRelayCommand MoveUpCommand { get; }

    public IRelayCommand MoveDownCommand { get; }

    public IRelayCommand AddDirectionCommand { get; }

    public IRelayCommand<DirectionRow> RemoveDirectionCommand { get; }

    /// <summary>The Dialogue Editor's window, while it's open.</summary>
    public DialogueEditorWindow? Window => _window;

    /// <summary>Opens the Dialogue Editor's window (or brings it to the front).</summary>
    public void Open()
    {
        if (_window is null)
        {
            _window = new DialogueEditorWindow(this) { Owner = _editorWindow.Main };
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        else
        {
            _window.Activate();
        }
    }

    /// <summary>Closes the Dialogue Editor's window, if it's open.</summary>
    public void Close() => _window?.Close();

    // ----- Editing -----

    /// <summary>Picks line <paramref name="index"/> (from 0) to edit and preview.</summary>
    public void SelectLine(int index)
    {
        if (Dialogue is not { } dialogue || index < 0 || index >= dialogue.Lines.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"The dialogue has {Dialogue?.Lines.Count ?? 0} lines, numbered from 0.");
        SelectedIndex = index;
    }

    /// <summary>Rewrites line <paramref name="index"/> as <paramref name="text"/>, as one undo step.</summary>
    public void SetLine(int index, string text) =>
        Rewrite("Edit Line", lines => lines[index] = text ?? "");

    /// <summary>
    /// Adds a line after the one picked (or at the end), said by the picked line's speaker, and picks it, as one undo step.
    /// Returns its number.
    /// </summary>
    public int AddLine(string text = "")
    {
        var at = HasLine ? SelectedIndex + 1 : Dialogue?.Lines.Count ?? 0;
        var line = text.Length > 0 ? text : Parts is { IsLine: true, Speaker: { Length: > 0 } who } ? who + ": ..." : "...";
        Rewrite("Add Line", lines => lines.Insert(at, line));
        SelectedIndex = at;
        return at;
    }

    /// <summary>Removes line <paramref name="index"/>, as one undo step.</summary>
    public void RemoveLine(int index)
    {
        Rewrite("Remove Line", lines => lines.RemoveAt(index));
        SelectedIndex = Math.Min(index, Lines.Count - 1);
    }

    /// <summary>Moves line <paramref name="index"/> up (-1) or down (1) a row, as one undo step.</summary>
    public void MoveLine(int index, int by)
    {
        var to = index + by;
        Rewrite("Move Line", lines =>
        {
            if (to < 0 || to >= lines.Count)
                return;
            (lines[index], lines[to]) = (lines[to], lines[index]);
        });
        SelectedIndex = Math.Clamp(to, 0, Lines.Count - 1);
    }

    /// <summary>
    /// Adds a stage direction to the line picked, after its others, as one undo step: <paramref name="kind"/> is Show, Hide,
    /// Move or Background; <paramref name="who"/> a character (or "all", to hide everyone); <paramref name="position"/>,
    /// <paramref name="expression"/> and <paramref name="background"/> names the stage has (empty leaves them);
    /// <paramref name="transition"/> cut, fade or slide (empty for the stage's own). Returns the tag, e.g.
    /// "[show Ava Left Happy]".
    /// </summary>
    public string AddDirection(string kind, string? who = null, string? position = null, string? expression = null, string? background = null, string? transition = null)
    {
        if (!HasLine)
            throw new InvalidOperationException("Pick a line to add a direction to.");
        if (!Enum.TryParse<DirectionKind>(kind, true, out var what))
            throw new ArgumentException($"A direction is Show, Hide, Move or Background, not \"{kind}\".", nameof(kind));
        StageTransition? how = Enum.TryParse<StageTransition>(transition, true, out var t) && transition != AsIs ? t : null;
        static string? Some(string? s) => string.IsNullOrWhiteSpace(s) || s == AsIs ? null : s.Trim();
        if (what == DirectionKind.Background ? Some(background) is null : Some(who) is null)
            throw new ArgumentException(what == DirectionKind.Background ? "Name the background to change to." : "Name the character it's for.");
        if (what == DirectionKind.Move && Some(position) is null)
            throw new ArgumentException("Name the position to move them to.", nameof(position));
        var direction = what == DirectionKind.Background
            ? new StageDirection(what, Background: Some(background), Transition: how)
            : new StageDirection(what, Some(who), what == DirectionKind.Hide ? null : Some(position), what == DirectionKind.Hide ? null : Some(expression), Transition: how);
        var tag = direction.ToTag();
        Change(p => p with { Tags = [.. p.Tags, tag] }, "Add Direction");
        return tag;
    }

    /// <summary>Removes the line picked's direction number <paramref name="index"/> (in <see cref="Directions"/>), as one undo step.</summary>
    public void RemoveDirection(int index)
    {
        if (index < 0 || index >= Directions.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"The line has {Directions.Count} directions, numbered from 0.");
        var tag = Directions[index].Tag;
        Change(p =>
        {
            var tags = p.Tags.ToList();
            tags.RemoveAt(tags.FindIndex(t => t == tag));
            return p with { Tags = tags };
        }, "Remove Direction");
    }

    // Rewrites the line picked from its parts.
    private void Change(Func<StageLines.LineParts, StageLines.LineParts> change, string description = "Edit Line")
    {
        if (Parts is not { } parts)
            return;
        var index = SelectedIndex;
        var text = StageLines.Join(change(parts));
        if (text != Dialogue!.Lines[index])
            Rewrite(description, lines => lines[index] = text);
    }

    // Sets the dialogue's Lines to a changed copy, as one undo step.
    private void Rewrite(string description, Action<List<string>> change)
    {
        if (Dialogue is not { } dialogue || _document is not { } document)
            throw new InvalidOperationException("Select an entity with a Dialogue to edit its lines.");
        var lines = dialogue.Lines.ToList();
        change(lines);
        if (lines.SequenceEqual(dialogue.Lines))
            return;
        var property = _projectSession.Components.Get(typeof(Dialogue)).Properties.Single(p => p.Name == nameof(Griff.Scenes.Dialogue.Lines));
        document.History.Seal();
        document.Execute(new CompositeEdit(description, [new SetPropertyEdit(dialogue, property, (IReadOnlyList<string>)lines)]));
        document.History.Seal();
    }

    // ----- Following the scene -----

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

    private void Follow()
    {
        var entity = _selection.Entity;
        // Picking a character or the stage keeps the stage's dialogue open.
        if (entity is not null && entity.GetComponent<Dialogue>() is null && Stage.StageOf(entity) is { } stage && stage.Entity!.GetComponent<Dialogue>() is not null)
            entity = stage.Entity;
        if (entity == _entity)
            return;
        _entity = entity;
        _selectedIndex = -1;
        Rebuild();
        if (Lines.Count > 0)
            SelectedIndex = 0;
    }

    private void Rebuild()
    {
        _rebuilding = true;
        try
        {
            Lines.Clear();
            if (Dialogue is { } dialogue)
            {
                for (var i = 0; i < dialogue.Lines.Count; i++)
                    Lines.Add(LineRow.Of(i, dialogue.Lines[i], Stage));
            }

            if (_selectedIndex >= Lines.Count)
                _selectedIndex = Lines.Count - 1;
            OnPropertyChanged(nameof(Entity));
            OnPropertyChanged(nameof(EntityName));
            OnPropertyChanged(nameof(HasDialogue));
            OnPropertyChanged(nameof(HasStage));
            OnPropertyChanged(nameof(Notice));
            OnPropertyChanged(nameof(HasNotice));
            OnPropertyChanged(nameof(Speakers));
            OnPropertyChanged(nameof(Characters));
            OnPropertyChanged(nameof(Positions));
            OnPropertyChanged(nameof(Backgrounds));
            OnPropertyChanged(nameof(NewExpressions));
            OnPropertyChanged(nameof(PreviewWidth));
            OnPropertyChanged(nameof(PreviewHeight));
            var characters = Characters;
            // Someone on the stage to begin with ("all" is only for hiding everyone).
            if (NewWho is null || !characters.Contains(NewWho))
                NewWho = characters.Count > 1 ? characters[0] : null;
            var backgrounds = Backgrounds;
            if (NewBackground is null || !backgrounds.Contains(NewBackground))
                NewBackground = backgrounds.Count > 0 ? backgrounds[0] : null;
            OnPropertyChanged(nameof(SelectedIndex));
            NotifyLine();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    public Entity? Entity => _entity;

    private void NotifyLine()
    {
        Directions.Clear();
        if (Parts is { } parts)
        {
            var i = 0;
            foreach (var tag in parts.Tags)
            {
                if (StageLines.DirectionIn(tag, Stage) is { } direction)
                    Directions.Add(new DirectionRow(i++, tag, direction.Describe()));
            }
        }

        OnPropertyChanged(nameof(HasLine));
        OnPropertyChanged(nameof(CanHaveSpeaker));
        OnPropertyChanged(nameof(RowText));
        OnPropertyChanged(nameof(Speaker));
        OnPropertyChanged(nameof(Expression));
        OnPropertyChanged(nameof(Expressions));
        OnPropertyChanged(nameof(Said));
        RefreshPreview();
        RemoveLineCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        AddDirectionCommand.NotifyCanExecuteChanged();
        AddLineCommand.NotifyCanExecuteChanged();
        NotifyStory();
    }

    private IReadOnlyList<string> ExpressionsOf(string? who) =>
        Stage?.CharacterNamed(who) is { } character ? StageLines.Names(character.Expressions) : [];

    // ----- Drawing the preview -----

    private void RefreshPreview()
    {
        PreviewFigures.Clear();
        PreviewBackground = null;
        (PreviewSpeaker, PreviewSaid) = ("", "");
        if (Stage is { } stage && Dialogue is { } dialogue && HasLine)
        {
            var state = StageLines.At(stage, dialogue, SelectedIndex);
            PreviewBackground = BackgroundBrush(stage, state.Background);
            var (width, height) = (PreviewWidth, PreviewHeight);
            foreach (var one in state.Cast.Where(c => c.Shown))
            {
                if (stage.CharacterNamed(one.Name) is not { } character || character.Entity!.GetComponent<Sprite>() is not { } sprite)
                    continue;
                var at = stage.PositionOf(one.Position) ?? StageDirectorHere(character, stage);
                var size = new System.Numerics.Vector2(Math.Abs(sprite.Size.X), Math.Abs(sprite.Size.Y));
                var picture = Picture(character, sprite, one.Expression);
                var dim = state.Speaker is null || one.Speaking ? 0 : Math.Clamp(stage.DimOthers, 0, 1);
                PreviewFigures.Add(new PreviewFigure(one.Name, picture, picture is null ? new SolidColorBrush(ToMedia(sprite.Tint)) : null,
                    (at.X - size.X / 2) * PreviewScale + width / 2, height / 2 - (at.Y + size.Y / 2) * PreviewScale,
                    size.X * PreviewScale, size.Y * PreviewScale, dim, one.Speaking));
            }

            PreviewSpeaker = state.Cast.Any(c => c.Speaking) || state.Speaker is not null ? state.Speaker ?? "" : "";
            if (PreviewSpeaker.Length == 0 && StageLines.Split(dialogue.Lines[SelectedIndex]).Speaker is { } named)
                PreviewSpeaker = named;
            PreviewSaid = state.Said;
        }

        OnPropertyChanged(nameof(PreviewBackground));
        OnPropertyChanged(nameof(PreviewSpeaker));
        OnPropertyChanged(nameof(PreviewSaid));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(Problems));
        OnPropertyChanged(nameof(HasProblems));
    }

    private static System.Numerics.Vector2 StageDirectorHere(Character character, Stage stage)
    {
        var world = character.Entity!.Transform.WorldPosition;
        var middle = stage.Entity!.Transform.WorldPosition;
        return new System.Numerics.Vector2(world.X - middle.X, world.Y - middle.Y);
    }

    private Brush? BackgroundBrush(Stage stage, string? name)
    {
        if (StageLines.Looks(stage.Backgrounds).FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) is not { Name: not null } look)
            return null;
        if (look.Image is { } image)
        {
            if (_art.Thumbnail(image) is not { } picture)
                return null;
            var brush = new ImageBrush(picture) { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
            brush.Freeze();
            return brush;
        }

        var gradient = new LinearGradientBrush(ToMedia(look.Top), ToMedia(look.Bottom), 90);
        gradient.Freeze();
        return gradient;
    }

    // What the character looks like with the expression: its picture, or its clip's first frame cut from the sheet.
    private ImageSource? Picture(Character character, Sprite sprite, string? expression)
    {
        var face = StageLines.Faces(character.Expressions).FirstOrDefault(f => string.Equals(f.Name, expression, StringComparison.OrdinalIgnoreCase));
        var image = face.Name is not null && face.IsImage ? face.Value : sprite.Image;
        if (_art.Thumbnail(image) is not { } picture)
            return null;
        var (columns, rows) = (Math.Max(1, sprite.Columns), Math.Max(1, sprite.Rows));
        if (columns * rows == 1)
            return picture;
        var frame = sprite.Frame;
        if (face.Name is not null && !face.IsImage && character.Entity!.GetComponent<Animation>() is { } animation && animation.HasClip(face.Value))
            frame = animation.ClipSettings(face.Value).First;
        var (w, h) = (picture.PixelWidth / columns, picture.PixelHeight / rows);
        frame = Math.Clamp(frame, 0, columns * rows - 1);
        var cut = new CroppedBitmap(picture, new Int32Rect(frame % columns * w, frame / columns * h, Math.Max(1, w), Math.Max(1, h)));
        cut.Freeze();
        return cut;
    }

    private static System.Windows.Media.Color ToMedia(Griff.Color c) => System.Windows.Media.Color.FromArgb(c.AByte, c.RByte, c.GByte, c.BByte);
}

/// <summary>A line of the dialogue as the Dialogue Editor lists it: its number, what it says and a summary of what it does.</summary>
public sealed record LineRow(int Index, string Text, string Kind, string Who, string Said, string Directions)
{
    public string Number => (Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public bool HasDirections => Directions.Length > 0;

    public bool HasWho => Who.Length > 0;

    public bool HasSaid => Said.Length > 0;

    internal static LineRow Of(int index, string text, Stage? stage)
    {
        var parts = StageLines.Split(text);
        var trimmed = text.TrimStart();
        var kind = !parts.IsLine ? trimmed.StartsWith('#') ? "part" : trimmed.StartsWith("->", StringComparison.Ordinal) ? "go to" : "line"
            : parts.Lead.Length > 0 ? "choice" : parts.Text.Length == 0 ? "directions" : "line";
        var who = parts.Speaker is { Length: > 0 } s ? s + (parts.Expression is { Length: > 0 } e ? $" ({e})" : "") : "";
        var directions = string.Join(" · ", parts.Directions(stage).Select(d => d.Describe()));
        return new LineRow(index, text, kind, who, parts.IsLine ? parts.Text : text, directions);
    }
}

/// <summary>One of a line's stage directions in the Dialogue Editor: where it is among them, its tag and what it does.</summary>
public sealed record DirectionRow(int Index, string Tag, string Description);

/// <summary>Someone on stage in the Dialogue Editor's preview: their picture (or color), where it's drawn, and how dimmed.</summary>
public sealed record PreviewFigure(string Name, ImageSource? Image, Brush? Fill, double Left, double Top, double Width, double Height, double Dim, bool Speaking)
{
    /// <summary>The picture as a mask, so dimming darkens only the figure.</summary>
    public Brush? Mask => Image is null ? null : new ImageBrush(Image) { Stretch = Stretch.Fill };
}
