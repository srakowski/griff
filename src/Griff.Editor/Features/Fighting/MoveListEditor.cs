using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Data;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Data;
using Griff.Modules.Fighting;
using Griff.Scenes;
using Animation = Griff.Scenes.Animation;

namespace Griff.Editor.Features.Fighting;

/// <summary>
/// The Move List: the selected fighter's moves, a row each with its input drawn as arrows (↓↘→ P), what the motion is
/// called and its frame data, and for the move picked its name, its motion picked from a list of the common ones
/// (quarter circles, dragon punches, charges) or typed, its button, the clip it plays, its startup, active and recovery
/// frames drawn as a bar with what it leaves on hit and on block, where it must be blocked, and what it cancels into.
/// Every change rewrites the fighter's Moves as one undo step, the same as typing them in the Inspector. Moves kept in a
/// table show too, to edit in Data &gt; Tables; Make Moves Table moves the fighter's own there. It follows the selection.
/// Agents work it the same way: <see cref="SelectMove"/>, set <see cref="MoveInput"/>, <see cref="MoveStartup"/> and the
/// rest, <see cref="AddMove"/>, and read <see cref="InputArrows"/> and <see cref="FrameSummary"/>.
/// </summary>
public sealed class MoveListEditor : ObservableObject
{
    // The most steps the frame bar draws a cell for.
    private const int MaxCells = 90;

    private static readonly Brush StartupBrush = Frozen(System.Windows.Media.Color.FromRgb(0x4C, 0xB8, 0x6A));
    private static readonly Brush ActiveBrush = Frozen(System.Windows.Media.Color.FromRgb(0xE0, 0x4A, 0x4A));
    private static readonly Brush RecoveryBrush = Frozen(System.Windows.Media.Color.FromRgb(0x4A, 0x86, 0xD8));

    private readonly IProjectSession _projectSession;
    private readonly ISelection _selection;
    private readonly TableStore _tables;
    private SceneDocument? _document;
    private Entity? _entity;
    private int _selectedIndex = -1;
    private bool _rebuilding;
    private MoveListWindow? _window;

    private readonly Core.EditorWindow _editorWindow;
    public MoveListEditor(ISceneDocuments sceneDocuments, IProjectSession projectSession, ISelection selection, TableStore tables, Core.EditorWindow editorWindow)
    {
        _projectSession = projectSession;
        _editorWindow = editorWindow;
        _selection = selection;
        _tables = tables;
        AddMoveCommand = new RelayCommand(() => AddMove(), () => Fighter is not null);
        RemoveMoveCommand = new RelayCommand(() => RemoveMove(SelectedIndex), () => IsOwnMove);
        MoveUpCommand = new RelayCommand(() => MoveMove(SelectedIndex, -1), () => IsOwnMove && SelectedIndex > 0);
        MoveDownCommand = new RelayCommand(() => MoveMove(SelectedIndex, 1), () => IsOwnMove && SelectedIndex < OwnCount - 1);
        MakeTableCommand = new RelayCommand(() => MakeTable(), () => Fighter is { Moves.Count: > 0 } && string.IsNullOrWhiteSpace(Fighter.MovesTable));

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
        tables.Changed += (_, _) => Rebuild();
        Track(sceneDocuments.Scene);
    }

    public string Title => "Move List";

    /// <summary>The fighter whose moves are listed: the selected entity's.</summary>
    public FightingCharacter? Fighter => _entity?.GetComponent<FightingCharacter>();

    public Entity? Entity => _entity;

    public string EntityName => _entity?.Name ?? "";

    public bool HasFighter => Fighter is not null;

    /// <summary>Why there's nothing to show, or null.</summary>
    public string? Notice => _entity is null ? "Select a fighter to edit its moves."
        : Fighter is null ? $"{_entity.Name} isn't a fighter. Add a Fighting Character to it, or make a pair to try from Entity > Genres > Fighting > Training Pair."
        : null;

    public bool HasNotice => Notice is not null;

    /// <summary>Its moves, its own and then its table's, a row each.</summary>
    public ObservableCollection<MoveRow> Moves { get; } = [];

    /// <summary>The move picked, from 0; -1 for none.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_rebuilding)
                return;
            value = Moves.Count == 0 ? -1 : Math.Clamp(value, -1, Moves.Count - 1);
            if (SetProperty(ref _selectedIndex, value))
                NotifyMove();
        }
    }

    public bool HasMove => SelectedIndex >= 0 && SelectedIndex < Moves.Count;

    /// <summary>True when the move picked is one of the fighter's own (not its table's), so it can be edited here.</summary>
    public bool IsOwnMove => HasMove && SelectedIndex < OwnCount;

    /// <summary>True when the move picked comes from the fighter's moves table.</summary>
    public bool IsTableMove => HasMove && !IsOwnMove;

    private int OwnCount => Fighter?.Moves.Count ?? 0;

    private FighterMove? Move => HasMove ? Moves[SelectedIndex].Move : null;

    // ----- The move picked -----

    /// <summary>The move picked as it's written in the fighter's Moves. Setting it rewrites the line, as one undo step.</summary>
    public string RowText
    {
        get => HasMove ? Moves[SelectedIndex].Text : "";
        set
        {
            if (IsOwnMove)
                SetMove(SelectedIndex, value);
        }
    }

    /// <summary>The move picked's name, e.g. "Fireball".</summary>
    public string MoveName
    {
        get => Move?.Name ?? "";
        set => Change(m => m with { Name = Clean(value) is { Length: > 0 } name ? name : m.Name }, "Rename Move");
    }

    /// <summary>The move picked's input in numpad notation, e.g. "236P" (6 is forward, 2 down; j. in front for the air, [4] for a charge).</summary>
    public string MoveInput
    {
        get => Move?.Input.ToString() ?? "";
        set
        {
            if (!Griff.Modules.Fighting.MoveInput.TryParse(value, out var input, out var problem))
                throw new ArgumentException($"\"{value}\" isn't an input: {problem}.", nameof(value));
            Change(m => m with { Input = input }, "Set Move Input");
        }
    }

    /// <summary>The motions to pick from: every direction, and the common special motions.</summary>
    public IReadOnlyList<MotionChoice> Motions { get; } = MotionChoice.All;

    /// <summary>The move picked's motion (its directions, with any charge), e.g. "236" or "[4]6"; picking one keeps its button.</summary>
    public string MoveMotion
    {
        get => Move is { } m ? (m.Input.Charge != 0 ? $"[{m.Input.Charge}]" : "") + m.Input.Motion : "";
        set => Change(m => m with { Input = Griff.Modules.Fighting.MoveInput.Parse((m.Input.Air ? "j." : "") + (value ?? "") + m.Input.Button) }, "Set Move Motion");
    }

    /// <summary>The buttons the fighter has, by name (its Buttons).</summary>
    public IReadOnlyList<string> Buttons => Kept([.. Fighter?.ButtonList.Select(b => b.Name) ?? []]);

    /// <summary>The move picked's button, e.g. "P".</summary>
    public string MoveButton
    {
        get => Move?.Input.Button ?? "";
        set => Change(m => m with { Input = Griff.Modules.Fighting.MoveInput.Parse(m.Input.ToString()[..^m.Input.Button.Length] + value) }, "Set Move Button");
    }

    /// <summary>True for a move done in the air (written j.).</summary>
    public bool MoveAir
    {
        get => Move?.Input.Air ?? false;
        set => Change(m => m with { Input = Griff.Modules.Fighting.MoveInput.Parse((value ? "j." : "") + m.Input.ToString().Replace("j.", "")) }, value ? "Make Move Air" : "Make Move Ground");
    }

    /// <summary>The clips to pick from: the fighter's animation's.</summary>
    public IReadOnlyList<string> Clips => Kept([.. Animation?.ClipList.Select(c => c.Name) ?? []]);

    /// <summary>
    /// The clip the move picked plays: one of <see cref="Clips"/> (as the animation spells it), or the name it looks for
    /// when the animation hasn't got it. A move plays the clip named like it (without spaces) unless it says otherwise.
    /// </summary>
    public string MoveClip
    {
        get => Move is { } m ? Clips.FirstOrDefault(c => string.Equals(c, m.ClipName, StringComparison.OrdinalIgnoreCase)) ?? m.ClipName : "";
        set => Change(m => m with
        {
            Clip = string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), m.Name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ? null : value.Trim(),
        }, "Set Move Clip");
    }

    /// <summary>Steps before it can hit; 0 for its clip's own.</summary>
    public int MoveStartup
    {
        get => Move?.Startup ?? 0;
        set => Change(m => m with { Startup = Steps(value) }, "Set Move Startup");
    }

    /// <summary>Steps it can hit for; 0 for its clip's own.</summary>
    public int MoveActive
    {
        get => Move?.Active ?? 0;
        set => Change(m => m with { Active = Steps(value) }, "Set Move Active");
    }

    /// <summary>Steps it takes to recover after; 0 for its clip's own.</summary>
    public int MoveRecovery
    {
        get => Move?.Recovery ?? 0;
        set => Change(m => m with { Recovery = Steps(value) }, "Set Move Recovery");
    }

    /// <summary>Where it must be blocked: Mid, Low or High.</summary>
    public IReadOnlyList<string> Heights { get; } = Enum.GetNames<MoveHeight>();

    /// <summary>Where the move picked must be blocked: Mid (either way), Low (crouching) or High (standing).</summary>
    public string MoveHits
    {
        get => (Move?.Hits ?? MoveHeight.Mid).ToString();
        set => Change(m => m with { Hits = Enum.TryParse<MoveHeight>(value, true, out var h) ? h : MoveHeight.Mid }, "Set Move Height");
    }

    /// <summary>Steps of blockstun a blocked hit gives; 0 for two thirds of its hitstun.</summary>
    public int MoveBlockstun
    {
        get => Move?.Blockstun ?? 0;
        set => Change(m => m with { Blockstun = Steps(value) }, "Set Move Blockstun");
    }

    /// <summary>How much of its damage a blocked hit does, 0 to 1; empty for the blocker's own Chip Damage.</summary>
    public string MoveChip
    {
        get => Move?.Chip is { } chip ? chip.ToString("0.###", CultureInfo.InvariantCulture) : "";
        set => Change(m => m with
        {
            Chip = string.IsNullOrWhiteSpace(value) ? null
                : float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var chip) && chip is >= 0 and <= 1 ? chip
                : throw new ArgumentException("Chip is from 0 to 1, e.g. 0.25.", nameof(value)),
        }, "Set Move Chip");
    }

    /// <summary>How fast it sends the fighter forward and up as it starts to hit, e.g. "2,0"; empty for none.</summary>
    public string MovePush
    {
        get => Move is { Push: var p } && p != System.Numerics.Vector2.Zero ? $"{p.X.ToString("0.###", CultureInfo.InvariantCulture)},{p.Y.ToString("0.###", CultureInfo.InvariantCulture)}" : "";
        set => Change(m => string.IsNullOrWhiteSpace(value) ? m with { Push = default }
            : FighterMove.Parse($"{m.Name}: {m.Input} push {value.Replace(" ", "")}") is var parsed ? m with { Push = parsed.Push } : m, "Set Move Push");
    }

    /// <summary>What it cancels into once it connects: move names, "special", "normal" or "any", with commas between; empty for nothing.</summary>
    public string MoveCancels
    {
        get => Move is { } m ? string.Join(", ", m.CancelList) : "";
        set => Change(m => m with { Cancels = [.. (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] }, "Set Move Cancels");
    }

    /// <summary>A sound played as it starts; empty for none.</summary>
    public string MoveSound
    {
        get => Move?.Sound ?? "";
        set => Change(m => m with { Sound = Clean(value) }, "Set Move Sound");
    }

    /// <summary>A message sent as it starts; empty for none.</summary>
    public string MoveMessage
    {
        get => Move?.Message ?? "";
        set => Change(m => m with { Message = Clean(value) }, "Set Move Message");
    }

    // ----- Its preview -----

    /// <summary>The move picked's input as arrows for a fighter facing right, e.g. "↓↘→ P".</summary>
    public string InputArrows => Move?.Input.Arrows() ?? "";

    /// <summary>What the move picked's input is called, e.g. "quarter circle forward + P".</summary>
    public string InputDescription => Move?.Input.Describe() ?? "";

    /// <summary>The move picked's frame data, e.g. "Startup 3 · Active 2 · Recovery 7 · 12 in all" ("from the clip" where it's the clip's own).</summary>
    public string FrameSummary { get; private set; } = "";

    /// <summary>What the move picked leaves its fighter on hit and on block, e.g. "+3 on hit, -2 on block", from its first hitbox's stun.</summary>
    public string Advantage { get; private set; } = "";

    /// <summary>The move picked's steps, a cell each (up to 90): startup green, active red, recovery blue.</summary>
    public ObservableCollection<FrameCell> FrameCells { get; } = [];

    /// <summary>What's wrong with the move picked, or with the fighter's moves: a line that doesn't read, a clip it hasn't, a cancel into nothing.</summary>
    public string Problems { get; private set; } = "";

    public bool HasProblems => Problems.Length > 0;

    /// <summary>The fighter's buttons and the actions they are, e.g. "P is Punch, K is Kick".</summary>
    public string ButtonsNote => Fighter is { } f ? string.Join(", ", f.ButtonList.Select(b => $"{b.Name} is {b.Action}")) : "";

    /// <summary>Where the fighter's table moves come from, e.g. "From the Moves table (Data > Tables)"; empty with none.</summary>
    public string TableNote => Fighter is { MovesTable: { Length: > 0 } table } ? $"Moves after the line come from the {table.Trim()} table: edit them in Data > Tables." : "";

    public IRelayCommand AddMoveCommand { get; }

    public IRelayCommand RemoveMoveCommand { get; }

    public IRelayCommand MoveUpCommand { get; }

    public IRelayCommand MoveDownCommand { get; }

    public IRelayCommand MakeTableCommand { get; }

    /// <summary>The Move List's window, while it's open.</summary>
    public MoveListWindow? Window => _window;

    /// <summary>Opens the Move List's window (or brings it to the front).</summary>
    public void Open()
    {
        if (_window is null)
        {
            _window = new MoveListWindow(this) { Owner = _editorWindow.Main };
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        else
        {
            _window.Activate();
        }
    }

    // ----- Editing -----

    /// <summary>Picks move <paramref name="index"/> (from 0, the fighter's own first, then its table's).</summary>
    public void SelectMove(int index)
    {
        if (index < 0 || index >= Moves.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"{EntityName} has {Moves.Count} moves, numbered from 0.");
        SelectedIndex = index;
    }

    /// <summary>Picks the move called <paramref name="name"/>.</summary>
    public void SelectMoveNamed(string name)
    {
        var index = Moves.ToList().FindIndex(r => string.Equals(r.Move?.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new ArgumentException($"{EntityName} has no move called \"{name}\"; its moves are {string.Join(", ", Moves.Select(r => r.Name))}.", nameof(name));
        SelectedIndex = index;
    }

    /// <summary>
    /// Adds a move after the picked one of the fighter's own (or at the end of them) and picks it, as one undo step:
    /// <paramref name="line"/> as written (e.g. "Fireball: 236P frames 12/4/30"), or a new jab. Returns its number.
    /// </summary>
    public int AddMove(string line = "")
    {
        var fighter = Fighter ?? throw new InvalidOperationException("Select a fighter to add a move to.");
        if (line.Length == 0)
        {
            var taken = fighter.Moves.Select(l => FighterMove.TryParse(l, out var m, out _) ? m.Name : "").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = Enumerable.Range(1, 99).Select(i => i == 1 ? "New Move" : $"New Move {i}").First(n => !taken.Contains(n));
            var button = fighter.ButtonList.Count > 0 ? fighter.ButtonList[0].Name : "P";
            line = $"{name}: 5{button}";
        }
        else if (!FighterMove.TryParse(line, out _, out var problem))
        {
            throw new ArgumentException($"\"{line}\" isn't a move: {problem}.", nameof(line));
        }

        var at = IsOwnMove ? SelectedIndex + 1 : fighter.Moves.Count;
        Rewrite("Add Move", lines => lines.Insert(at, line));
        SelectedIndex = at;
        return at;
    }

    /// <summary>Rewrites the fighter's own move <paramref name="index"/> as <paramref name="line"/>, as one undo step.</summary>
    public void SetMove(int index, string line)
    {
        if (index < 0 || index >= OwnCount)
            throw new ArgumentOutOfRangeException(nameof(index), $"{EntityName} has {OwnCount} moves of its own, numbered from 0.");
        Rewrite("Edit Move", lines => lines[index] = (line ?? "").Trim());
    }

    /// <summary>Removes the fighter's own move <paramref name="index"/>, as one undo step.</summary>
    public void RemoveMove(int index)
    {
        if (index < 0 || index >= OwnCount)
            throw new ArgumentOutOfRangeException(nameof(index), $"{EntityName} has {OwnCount} moves of its own, numbered from 0.");
        Rewrite("Remove Move", lines => lines.RemoveAt(index));
        SelectedIndex = Math.Min(index, Moves.Count - 1);
    }

    /// <summary>Moves the fighter's own move <paramref name="index"/> up (-1) or down (1): of two that fit, the one listed first wins.</summary>
    public void MoveMove(int index, int by)
    {
        var to = index + by;
        if (index < 0 || index >= OwnCount || to < 0 || to >= OwnCount)
            return;
        Rewrite("Move Move", lines => (lines[index], lines[to]) = (lines[to], lines[index]));
        SelectedIndex = to;
    }

    /// <summary>
    /// Moves the fighter's own moves into a new table (Assets/Tables, "<c>Name</c> Moves"), a row each with columns Input,
    /// Clip, Startup, Active, Recovery, Hits, Blockstun, Chip, Push, Cancels, Message and Sound, and sets its Moves Table
    /// to it, so they're edited like a spreadsheet. Returns the table's name.
    /// </summary>
    public string MakeTable()
    {
        var fighter = Fighter ?? throw new InvalidOperationException("Select a fighter to make a moves table for.");
        if (!string.IsNullOrWhiteSpace(fighter.MovesTable))
            throw new InvalidOperationException($"{EntityName} has a moves table already ({fighter.MovesTable}).");
        if (_document is not { } document)
            throw new InvalidOperationException("No scene is open.");
        var name = Enumerable.Range(1, 99).Select(i => i == 1 ? $"{EntityName} Moves" : $"{EntityName} Moves {i}").First(n => _tables.NameProblem(n) is null);
        var moves = FighterMove.ParseAll(fighter.Moves);
        TableColumn[] columns =
        [
            new("Input", ColumnType.Text), new("Clip", ColumnType.Text), new("Startup", ColumnType.WholeNumber),
            new("Active", ColumnType.WholeNumber), new("Recovery", ColumnType.WholeNumber),
            new("Hits", ColumnType.Choice) { Choices = Enum.GetNames<MoveHeight>() }, new("Blockstun", ColumnType.WholeNumber),
            new("Chip"), new("Push", ColumnType.Text), new("Cancels", ColumnType.Text), new("Message", ColumnType.Text), new("Sound", ColumnType.Sound),
        ];
        var rows = moves.Select(m => (m.Name, (IReadOnlyList<TableValue>)
        [
            TableValue.Of(m.Input.ToString()), m.Clip is null ? TableValue.Empty : TableValue.Of(m.Clip),
            TableValue.Of((long)m.Startup), TableValue.Of((long)m.Active), TableValue.Of((long)m.Recovery),
            TableValue.Of(m.Hits.ToString()), TableValue.Of((long)m.Blockstun),
            m.Chip is { } chip ? TableValue.Of((double)chip) : TableValue.Empty,
            m.Push == default ? TableValue.Empty : TableValue.Of($"{m.Push.X.ToString(CultureInfo.InvariantCulture)},{m.Push.Y.ToString(CultureInfo.InvariantCulture)}"),
            m.CancelList.Count == 0 ? TableValue.Empty : TableValue.Of(string.Join(", ", m.CancelList)),
            m.Message is null ? TableValue.Empty : TableValue.Of(m.Message), m.Sound is null ? TableValue.Empty : TableValue.Of(m.Sound),
        ]));
        _tables.Create(name);
        _tables.Commit(new Table(name, columns, rows), $"Make {name}");
        var component = _projectSession.Components.Get(typeof(FightingCharacter));
        document.History.Seal();
        document.Execute(new CompositeEdit("Make Moves Table",
        [
            new SetPropertyEdit(fighter, component.Properties.Single(p => p.Name == nameof(FightingCharacter.Moves)), (IReadOnlyList<string>)[]),
            new SetPropertyEdit(fighter, component.Properties.Single(p => p.Name == nameof(FightingCharacter.MovesTable)), name),
        ]));
        document.History.Seal();
        return name;
    }

    // Rewrites the move picked from a change to it.
    private void Change(Func<FighterMove, FighterMove> change, string description)
    {
        if (!IsOwnMove || Move is not { } move)
            return;
        var index = SelectedIndex;
        var line = change(move).ToString();
        if (line != Fighter!.Moves[index])
            Rewrite(description, lines => lines[index] = line);
    }

    // Sets the fighter's Moves to a changed copy, as one undo step.
    private void Rewrite(string description, Action<List<string>> change)
    {
        if (Fighter is not { } fighter || _document is not { } document)
            throw new InvalidOperationException("Select a fighter to edit its moves.");
        var lines = fighter.Moves.ToList();
        change(lines);
        if (lines.SequenceEqual(fighter.Moves))
            return;
        var property = _projectSession.Components.Get(typeof(FightingCharacter)).Properties.Single(p => p.Name == nameof(FightingCharacter.Moves));
        document.History.Seal();
        document.Execute(new CompositeEdit(description, [new SetPropertyEdit(fighter, property, (IReadOnlyList<string>)lines)]));
        document.History.Seal();
    }

    private static int Steps(int value) => value is >= 0 and <= 999 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Steps are from 0 to 999.");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ----- Following the scene -----

    private Animation? Animation => _entity?.GetComponent<Animation>();

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
        if (entity == _entity)
            return;
        _entity = entity;
        _selectedIndex = -1;
        Rebuild();
        if (Moves.Count > 0)
            SelectedIndex = 0;
    }

    private void Rebuild()
    {
        _rebuilding = true;
        try
        {
            Moves.Clear();
            if (Fighter is { } fighter)
            {
                var all = new List<FighterMove>();
                for (var i = 0; i < fighter.Moves.Count; i++)
                {
                    var line = fighter.Moves[i];
                    var move = FighterMove.TryParse(line, out var parsed, out var problem) ? parsed : null;
                    Moves.Add(MoveRow.Of(i, line, move, problem, fromTable: false, this));
                    if (move is not null)
                        all.Add(move);
                }

                if (!string.IsNullOrWhiteSpace(fighter.MovesTable) && _tables.Find(fighter.MovesTable.Trim()) is { } table)
                {
                    foreach (var row in table.Rows)
                    {
                        var move = FighterMove.TryFromRow(row, out var parsed, out var problem) ? parsed : null;
                        Moves.Add(MoveRow.Of(Moves.Count, row.Name, move, problem, fromTable: true, this));
                    }
                }
            }

            if (_selectedIndex >= Moves.Count)
                _selectedIndex = Moves.Count - 1;
            OnPropertyChanged(nameof(Entity));
            OnPropertyChanged(nameof(EntityName));
            OnPropertyChanged(nameof(HasFighter));
            OnPropertyChanged(nameof(Notice));
            OnPropertyChanged(nameof(HasNotice));
            OnPropertyChanged(nameof(Buttons));
            OnPropertyChanged(nameof(Clips));
            OnPropertyChanged(nameof(ButtonsNote));
            OnPropertyChanged(nameof(TableNote));
            OnPropertyChanged(nameof(SelectedIndex));
            NotifyMove();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void NotifyMove()
    {
        FrameCells.Clear();
        (FrameSummary, Advantage) = ("", "");
        var problems = new List<string>();
        if (HasMove && Moves[SelectedIndex].Problem is { } problem)
            problems.Add(problem);
        if (Move is { } move)
        {
            var timing = TimingOf(move);
            for (var i = 0; i < Math.Min(MaxCells, timing.Total); i++)
            {
                var phase = timing.PhaseAt(i);
                FrameCells.Add(new FrameCell(i + 1, phase.ToString(), phase switch { MovePhase.Startup => StartupBrush, MovePhase.Active => ActiveBrush, _ => RecoveryBrush }));
            }

            string Part(string name, int steps, bool fromClip) => $"{name} {steps}{(fromClip && timing.HasClip ? " (clip)" : "")}";
            FrameSummary = $"{Part("Startup", timing.Startup, timing.StartupFromClip)} · {Part("Active", timing.Active, timing.ActiveFromClip)} · " +
                           $"{Part("Recovery", timing.Recovery, timing.RecoveryFromClip)} · {timing.Total} in all";
            if (FirstHit(move, timing) is { } hit)
            {
                var blockstun = move.Blockstun > 0 ? move.Blockstun : (int)MathF.Round(hit.Stun * 2 / 3f);
                // Hit on its first active step: the target is free the step after its stun; the fighter once its move is done.
                Advantage = $"{Signed(hit.Stun + 1 - timing.Active - timing.Recovery)} on hit, {Signed(blockstun + 1 - timing.Active - timing.Recovery)} on block · {hit.Damage:0.##} damage";
            }
            else if (timing.HasClip)
            {
                problems.Add($"Its clip {move.ClipName} has no hitbox, so it can't hit: draw one in the Clip Editor.");
            }

            if (!timing.HasClip)
                problems.Add(Animation is null ? "The fighter has no Animation to play the move's clip." : $"The fighter's animation has no clip called {move.ClipName}.");
            if (Fighter is { } fighter && !fighter.ButtonList.Any(b => string.Equals(b.Name, move.Input.Button, StringComparison.OrdinalIgnoreCase)))
                problems.Add($"{move.Input.Button} isn't one of its Buttons ({string.Join(", ", fighter.ButtonList.Select(b => b.Name))}).");
            foreach (var cancel in move.CancelList.Where(c => c is not ("special" or "normal" or "any") && !Moves.Any(r => string.Equals(r.Move?.Name, c, StringComparison.OrdinalIgnoreCase))))
                problems.Add($"It cancels into {cancel}, which isn't one of its moves.");
        }

        Problems = string.Join("\n", problems);
        OnPropertyChanged(nameof(HasMove));
        OnPropertyChanged(nameof(IsOwnMove));
        OnPropertyChanged(nameof(IsTableMove));
        OnPropertyChanged(nameof(RowText));
        OnPropertyChanged(nameof(MoveName));
        OnPropertyChanged(nameof(MoveInput));
        OnPropertyChanged(nameof(MoveMotion));
        OnPropertyChanged(nameof(MoveButton));
        OnPropertyChanged(nameof(MoveAir));
        OnPropertyChanged(nameof(MoveClip));
        OnPropertyChanged(nameof(MoveStartup));
        OnPropertyChanged(nameof(MoveActive));
        OnPropertyChanged(nameof(MoveRecovery));
        OnPropertyChanged(nameof(MoveHits));
        OnPropertyChanged(nameof(MoveBlockstun));
        OnPropertyChanged(nameof(MoveChip));
        OnPropertyChanged(nameof(MovePush));
        OnPropertyChanged(nameof(MoveCancels));
        OnPropertyChanged(nameof(MoveSound));
        OnPropertyChanged(nameof(MoveMessage));
        OnPropertyChanged(nameof(InputArrows));
        OnPropertyChanged(nameof(InputDescription));
        OnPropertyChanged(nameof(FrameSummary));
        OnPropertyChanged(nameof(Advantage));
        OnPropertyChanged(nameof(Problems));
        OnPropertyChanged(nameof(HasProblems));
        AddMoveCommand.NotifyCanExecuteChanged();
        RemoveMoveCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        MakeTableCommand.NotifyCanExecuteChanged();
    }

    internal MoveTiming TimingOf(FighterMove move) =>
        FightingCharacter.TimingOf(move, Animation, _entity?.GetComponent<Hitboxes>(), _projectSession.Project?.StepRate ?? 60);

    // The move's first hitbox on its first active frame.
    private FrameBox? FirstHit(FighterMove move, MoveTiming timing)
    {
        if (timing.FirstHit < 0 || _entity?.GetComponent<Hitboxes>() is not { } boxes || Animation?.ClipList.FirstOrDefault(c => string.Equals(c.Name, move.ClipName, StringComparison.OrdinalIgnoreCase)) is not { } clip)
            return null;
        foreach (var box in boxes.On(clip.Name, timing.FirstHit))
        {
            if (box.IsHit)
                return box;
        }

        return null;
    }

    private static string Signed(int n) => n > 0 ? $"+{n}" : n.ToString(CultureInfo.InvariantCulture);

    // The lists keep the same instance while what's in them doesn't change, so a list box bound to one keeps its pick.
    private readonly Dictionary<string, IReadOnlyList<string>> _lists = [];

    private IReadOnlyList<string> Kept(IReadOnlyList<string> now, [System.Runtime.CompilerServices.CallerMemberName] string list = "")
    {
        if (_lists.TryGetValue(list, out var before) && before.SequenceEqual(now))
            return before;
        _lists[list] = now;
        return now;
    }

    private static SolidColorBrush Frozen(System.Windows.Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>A move as the Move List lists it: its name, its input as arrows, what that's called, and its frame data.</summary>
public sealed record MoveRow(int Index, string Text, FighterMove? Move, string? Problem, bool FromTable, string Arrows, string Description, string Frames)
{
    public string Name => Move?.Name ?? Text;

    public bool HasProblem => Problem is not null;

    internal static MoveRow Of(int index, string text, FighterMove? move, string? problem, bool fromTable, MoveListEditor editor)
    {
        if (move is null)
            return new MoveRow(index, text, null, problem is null ? null : $"\"{text}\" doesn't read: {problem}.", fromTable, "", "", "");
        var timing = editor.TimingOf(move);
        var frames = $"{timing.Startup}/{timing.Active}/{timing.Recovery}" + (move.Hits == MoveHeight.Mid ? "" : $" · {move.Hits.ToString().ToLowerInvariant()}") +
                     (fromTable ? " · table" : "");
        return new MoveRow(index, text, move, null, fromTable, move.Input.Arrows(), move.Input.Describe(), frames);
    }
}

/// <summary>A step of a move in the Move List's frame bar: its number, its phase and its color.</summary>
public sealed record FrameCell(int Step, string Phase, Brush Fill)
{
    public string Tip => $"Step {Step}: {Phase.ToLowerInvariant()}";
}

/// <summary>A motion to pick in the Move List: its digits ("236", "[4]6") and how it reads ("↓↘→  quarter circle forward").</summary>
public sealed record MotionChoice(string Motion, string Label)
{
    /// <summary>Every direction on its own (and none), then the common special motions.</summary>
    public static IReadOnlyList<MotionChoice> All { get; } =
    [
        new("", "any direction"),
        .. new[] { "5", "6", "4", "2", "3", "1", "8" }.Select(d => new MotionChoice(d, $"{MoveInput.Arrow(d[0] - '0')}  {MoveInput.DirectionName(d[0] - '0')} ({d})")),
        .. MoveInput.Common.Select(c => new MotionChoice(c.Motion, $"{MoveInput.Parse(c.Motion + "P").Arrows()[..^2]}  {c.Name} ({c.Motion})")),
    ];
}
