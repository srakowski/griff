using System.Globalization;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Runtime;

// Reading dialogue: the lines read before (remembered from one run of the game to the next), skipping through them,
// reading on by itself, and the logs of the lines said (see Backlog).
public sealed partial class Game
{
    /// <summary>Where the lines read before are kept in <see cref="SaveData"/>, so skipping knows them next time too.</summary>
    public const string ReadLinesKey = "griff.read";

    // Seconds between lines while skipping.
    internal const float SkipEvery = 0.05f;

    // The read lines are written to the save data at most this often, in seconds, while new ones are being read.
    private const double ReadLinesWriteEvery = 1;

    private bool _skipUnread;
    private float _autoSpeed = 0.5f;
    private bool _skipping;
    private float _skipClock = SkipEvery;
    private float _autoClock;
    private (Dialogue? Dialogue, int Line) _autoLine;
    private HashSet<uint>? _readLines;
    private SaveData? _readLinesOf;
    private bool _readLinesChanged;
    private double _readLinesWrittenAt = double.NegativeInfinity;

    /// <summary>
    /// True while dialogue skips: each line read before is shown and passed in a moment, until a line not read before
    /// (unless <see cref="SkipUnread"/>), choices or the end of the talk, which turn it off. A Toggle set to Skip, or a
    /// script, turns it on; a visual novel stage's Skip action skips while it's held.
    /// </summary>
    public bool Skipping
    {
        get => _skipping;
        set
        {
            _skipping = value;
            _skipClock = SkipEvery;
        }
    }

    /// <summary>
    /// True while dialogue reads on by itself: once a line has typed out, the next comes after a pause as long as the line
    /// (see <see cref="AutoSpeed"/>). It waits at choices. A Toggle set to Auto, or a script, turns it on and off; so does
    /// a visual novel stage's Auto action. It stays on from one talk to the next.
    /// </summary>
    public bool AutoReading
    {
        get => _autoReading;
        set
        {
            _autoReading = value;
            _autoClock = 0;
        }
    }

    private bool _autoReading;

    /// <summary>
    /// The player's setting for whether skipping goes through lines they haven't read yet too, rather than stopping at
    /// the first. Kept in <see cref="SaveData"/>; a Toggle set to Skip Unread changes it.
    /// </summary>
    public bool SkipUnread
    {
        get => _skipUnread;
        set => _skipUnread = value;
    }

    /// <summary>
    /// The player's setting for how quickly <see cref="AutoReading"/> reads on, from 0 (slowly) to 1 (quickly); 0.5 to
    /// begin with. Kept in <see cref="SaveData"/>; a Slider set to Auto Speed changes it.
    /// </summary>
    public float AutoSpeed
    {
        get => _autoSpeed;
        set => _autoSpeed = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5f;
    }

    /// <summary>True while dialogue is skipping: <see cref="Skipping"/>, or a skip key held.</summary>
    public bool IsSkipping => _skipping || SkipHeld;

    // Set each frame by what reads a skip key (a visual novel stage's Skip action), while it's held.
    internal bool SkipHeld { get; set; }

    /// <summary>
    /// Seconds <see cref="AutoReading"/> waits after <paramref name="said"/> has typed out before reading on: longer for
    /// a longer line, shorter the higher <see cref="AutoSpeed"/> is.
    /// </summary>
    public float AutoDelay(string? said) => (0.8f + 0.05f * (said?.Length ?? 0)) * MathF.Pow(4, 0.5f - _autoSpeed);

    /// <summary>
    /// True if row <paramref name="row"/> of <paramref name="dialogue"/>'s lines has been shown before, in this run of
    /// the game or an earlier one: skipping goes through lines read before.
    /// </summary>
    public bool HasRead(Dialogue dialogue, int row) =>
        row >= 0 && row < dialogue.Lines.Count && ReadLines().Contains(LineId(dialogue, row));

    // A line, known by its dialogue's entity and its words, so a line changed since reads as new.
    private static uint LineId(Dialogue dialogue, int row)
    {
        var hash = 2166136261u;
        foreach (var b in (dialogue.Entity?.Id ?? Guid.Empty).ToByteArray())
            hash = (hash ^ b) * 16777619u;
        foreach (var c in dialogue.Lines[row] ?? "")
            hash = (hash ^ c) * 16777619u;
        return hash;
    }

    private HashSet<uint> ReadLines()
    {
        if (_readLines is not null && _readLinesOf == SaveData)
            return _readLines;
        _readLinesOf = SaveData;
        _readLinesChanged = false;
        _readLines = [];
        foreach (var word in (SaveData.GetText(ReadLinesKey) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (uint.TryParse(word, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                _readLines.Add(id);
        }

        return _readLines;
    }

    // As a line is shown: whether it was read before (for skipping), and now it has been; and it goes in the logs.
    private void NoteLineShown(Dialogue dialogue, int row, bool said, bool resume)
    {
        _levels.LineReadBefore = !said || HasRead(dialogue, row);
        if (said && ReadLines().Add(LineId(dialogue, row)))
            _readLinesChanged = true;
        if (!resume && said && dialogue.Said is { } words)
            AddToLogs(dialogue.Speaker is { } speaker && speaker.ShownName.Length > 0 ? $"{speaker.ShownName}: {words}" : "\\" + words);
    }

    // Every log in the scene, open or not, gets the line.
    private void AddToLogs(string line)
    {
        foreach (var log in Scene.All<Backlog>())
            log.Add(line);
    }

    // The lines read are written to the save data now and then while new ones are read (writing it all each line would be
    // a lot), and at once when asked.
    private void SaveReadLines(bool now = false)
    {
        if (!_readLinesChanged || _readLines is null || _readLinesOf != SaveData)
            return;
        if (!now && Time.RealTotal - _readLinesWrittenAt < ReadLinesWriteEvery)
            return;
        _readLinesChanged = false;
        _readLinesWrittenAt = Time.RealTotal;
        SaveData.Set(ReadLinesKey, string.Join(' ', _readLines.Select(id => id.ToString("x8", CultureInfo.InvariantCulture))));
    }

    // After the dialogues' step: skipping and reading on by itself move the talk on, in real time.
    private void RunReading()
    {
        if (_levels.Talking is not { } talking)
        {
            _skipping = false;
            _autoClock = 0;
            _autoLine = default;
            return;
        }

        if ((talking, talking.Line) != _autoLine)
            (_autoLine, _autoClock) = ((talking, talking.Line), 0);
        var typing = IsTyping(talking);
        if (IsSkipping)
        {
            _skipClock += Time.UnscaledDelta;
            if (_skipClock < SkipEvery)
                return;
            _skipClock = 0;
            if (talking.Choices.Count > 0 || (!_levels.LineReadBefore && !_skipUnread))
            {
                // Here's where the player has to read for themselves.
                _skipping = false;
                return;
            }

            if (typing)
                LineText(talking)?.Entity?.GetComponent<Typewriter>()?.Skip();
            AdvanceDialogue(talking);
            return;
        }

        _skipClock = SkipEvery;
        if (!_autoReading || typing || talking.Choices.Count > 0)
        {
            _autoClock = 0;
            return;
        }

        _autoClock += Time.UnscaledDelta;
        if (_autoClock < AutoDelay(talking.Said))
            return;
        _autoClock = 0;
        AdvanceDialogue(talking);
    }

    // ----- Logs on screen (Backlog)

    // The logs showing last frame (to scroll a newly opened one to its newest line), how long a scroll key has been held,
    // and where a drag over one was.
    private readonly HashSet<Backlog> _logsShown = [];
    private float _logKeyHeld;
    private float? _logDragY;

    // With the menus: a log's panel opens when the wheel turns up over the game, and a log showing scrolls.
    private void RunBacklogs()
    {
        var logs = Scene.Active<Backlog>();
        if (logs.Count == 0 && _logsShown.Count == 0 && Input.Scroll <= 0)
            return;
        var top = TopPanel;
        if (top is null && Input.Scroll > 0 && Input.IsMouseInView && !MouseOnUi && OpenLogOnWheel())
            return;

        _logsShown.RemoveWhere(l => !l.Entity!.IsActive || l.Entity.Scene != Scene);
        foreach (var log in logs)
        {
            // Only the one on the panel on top (or on none) takes the keys.
            if (_logsShown.Add(log))
            {
                log.Scroll = 0;
                continue;
            }

            if (top is not null && !Inside(log.Entity!, top))
                continue;
            ScrollLog(log, PanelOf(log.Entity!));
        }
    }

    private bool OpenLogOnWheel()
    {
        foreach (var log in Scene.All<Backlog>())
        {
            if (!log.OpenOnWheel || log.Entity!.IsActive || PanelOf(log.Entity) is not { } panel || !FlagHolds(panel.NeedsFlag))
                continue;
            if (panel.Entity!.Parent is { IsActive: false })
                continue;
            OpenPanel(panel);
            // Newest at the bottom; the next turn of the wheel scrolls it.
            log.Scroll = 0;
            _logsShown.Add(log);
            return true;
        }

        return false;
    }

    private void ScrollLog(Backlog log, Panel? panel)
    {
        var rows = 0f;
        // The wheel: up goes back, and down past the newest line closes the log.
        if (Input.Scroll != 0)
        {
            if (Input.Scroll < 0 && log.Scroll <= 0 && log.OpenOnWheel && panel is not null)
            {
                ClosePanel(panel);
                return;
            }

            rows += Input.Scroll * 3;
        }

        // The arrow keys, D-pad and stick a row at a time, held to keep going; Page Up and Down a page; Home and End.
        var up = Input.IsDown(Key.Up) || Input.IsDown(Key.W) || Input.IsDown(GamepadButton.DPadUp) || Input.LeftStick.Y > NavigateStick;
        var down = Input.IsDown(Key.Down) || Input.IsDown(Key.S) || Input.IsDown(GamepadButton.DPadDown) || Input.LeftStick.Y < -NavigateStick;
        if (up != down)
        {
            var sign = up ? 1 : -1;
            if (_logKeyHeld == 0)
                rows += sign;
            _logKeyHeld += Time.UnscaledDelta;
            if (_logKeyHeld > CursorRepeatDelay)
                rows += sign * Time.UnscaledDelta / CursorRepeatEvery;
        }
        else
        {
            _logKeyHeld = 0;
        }

        if (Input.WasPressed(Key.PageUp))
            rows += log.RowsShown;
        if (Input.WasPressed(Key.PageDown))
            rows -= log.RowsShown;
        if (Input.WasPressed(Key.Home))
            rows += log.MostScroll;
        if (Input.WasPressed(Key.End))
            rows -= log.MostScroll;

        // Dragging it (a finger on a touch screen): the lines move with the pointer.
        var area = new Box(Flat(log.Entity!.Transform.WorldPosition), System.Numerics.Vector2.Abs(log.Size) / 2);
        if (Input.WasPressed(MouseButton.Left) && Input.IsMouseInView && area.Contains(Input.MouseWorld))
            _logDragY = Input.MouseWorld.Y;
        if (_logDragY is { } from)
        {
            if (Input.IsDown(MouseButton.Left))
            {
                rows += (from - Input.MouseWorld.Y) / log.RowHeight;
                _logDragY = Input.MouseWorld.Y;
            }
            else
            {
                _logDragY = null;
            }
        }

        log.Scroll = Math.Clamp(log.Scroll + rows, 0, log.MostScroll);
    }

    // The panel an entity is on (or is), if any.
    private static Panel? PanelOf(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Panel>() is { } panel)
                return panel;
        }

        return null;
    }

    // ----- Save slots on screen (SaveSlot)

    // With the menus: each slot showing has the game saved in it.
    private void ShowSaveSlots()
    {
        var slots = Scene.Active<SaveSlot>();
        if (slots.Count == 0)
            return;
        foreach (var slot in slots)
        {
            var saved = string.IsNullOrWhiteSpace(slot.Slot) ? null : SavedGameIn(slot.Slot);
            (slot.HasSave, slot.Picture, slot.PictureAspect) = (saved is not null, saved?.Picture, saved?.PictureAspect ?? 0);
            slot.Description = saved is null ? slot.EmptyText ?? "" : Shorten(saved.Describe(slot.Format ?? ""), slot.PictureSize.X, slot.TextSize);
        }
    }

    // Each line no wider than `width`, and at most three of them.
    private static string Shorten(string text, float width, float size)
    {
        var lines = text.Split('\n').SelectMany(l => Wrap(l, MathF.Abs(width), size).Split('\n')).ToList();
        return lines.Count <= 3 ? string.Join('\n', lines) : string.Join('\n', lines.Take(3)) + "...";
    }

    // The slot a save, load or delete button works on: its own, or else that of the save slot it's on or inside.
    internal static string? SlotOf(Button button)
    {
        if (!string.IsNullOrWhiteSpace(button.Slot))
            return button.Slot.Trim();
        for (var e = button.Entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<SaveSlot>() is { Slot: { } slot } && !string.IsNullOrWhiteSpace(slot))
                return slot.Trim();
        }

        return null;
    }
}
