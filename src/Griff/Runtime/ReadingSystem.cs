using Griff.Input;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Moves conversations on by themselves: skipping through lines read before and reading on after a pause, and keeps the
/// logs of the lines said (Backlog). How the player likes it and the lines they've read come from a system that keeps
/// them (IReadingPreferences).
/// </summary>
internal sealed class ReadingSystem : GameSystem
{
    // Seconds between lines while skipping.
    internal const float SkipEvery = 0.05f;

    private bool _skipping;

    // Whether the line showing had been read before it was shown this time: skipping stops at one that hadn't.
    private bool _lineReadBefore;

    private float _skipClock = SkipEvery;

    private float _autoClock;

    private (Dialogue? Dialogue, int Line) _autoLine;

    private bool _autoReading;

    private bool _skipHeld;

    private IReadingPreferences? _preferences;

    private bool _preferencesFound;

    // ----- Logs on screen (Backlog)

    // The logs showing last frame (to scroll a newly opened one to its newest line), how long a scroll key has been held,
    // and where a drag over one was.
    private readonly HashSet<Backlog> _logsShown = [];

    private float _logKeyHeld;

    private float? _logDragY;

    public override string Name => "Reading";

    public bool Skipping
    {
        get => _skipping;
        set
        {
            _skipping = value;
            _skipClock = SkipEvery;
        }
    }

    public bool AutoReading
    {
        get => _autoReading;
        set
        {
            _autoReading = value;
            _autoClock = 0;
        }
    }

    public bool IsSkipping => _skipping || SkipHeld;

    public bool SkipHeld
    {
        get => _skipHeld;
        set => _skipHeld = value;
    }

    public float AutoDelay(string? said) => (0.8f + 0.05f * (said?.Length ?? 0)) * MathF.Pow(4, 0.5f - (Preferences?.AutoSpeed ?? 0.5f));

    // What the player prefers and has read, kept by a system that says so (the Visual Novels module's), found once.
    private IReadingPreferences? Preferences
    {
        get
        {
            if (!_preferencesFound)
                (_preferences, _preferencesFound) = (Game.Systems.OfType<IReadingPreferences>().FirstOrDefault(), true);
            return _preferences;
        }
    }

    // As a line is shown: whether it was read before (for skipping), and now it has been; and it goes in the logs.
    internal void NoteLineShown(Dialogue dialogue, int row, bool said, bool resume)
    {
        _lineReadBefore = !said || (Preferences?.Read(dialogue, row) ?? false);
        if (!resume && said && dialogue.Said is { } words)
            AddToLogs(dialogue.Speaker is { } speaker && speaker.ShownName.Length > 0 ? $"{speaker.ShownName}: {words}" : "\\" + words);
    }

    // Every log in the scene, open or not, gets the line.
    internal void AddToLogs(string line)
    {
        foreach (var log in Game.Scene.AllNow<Backlog>())
            log.Add(line);
    }

    // After the dialogues' step: skipping and reading on by itself move the talk on, in real time.
    internal void RunReading()
    {
        if (Game.Talking is not { } talking)
        {
            _skipping = false;
            _autoClock = 0;
            _autoLine = default;
            return;
        }

        if ((talking, talking.Line) != _autoLine)
            (_autoLine, _autoClock) = ((talking, talking.Line), 0);
        var typing = Game.IsTyping(talking);
        if (IsSkipping)
        {
            _skipClock += Game.Time.UnscaledDelta;
            if (_skipClock < SkipEvery)
                return;
            _skipClock = 0;
            if (talking.Choices.Count > 0 || (!_lineReadBefore && Preferences?.SkipUnread != true))
            {
                // Here's where the player has to read for themselves.
                _skipping = false;
                return;
            }

            if (typing)
                Game.LevelSystem.LineText(talking)?.Entity?.GetComponent<Typewriter>()?.Skip();
            Game.AdvanceDialogue(talking);
            return;
        }

        _skipClock = SkipEvery;
        if (!_autoReading || typing || talking.Choices.Count > 0)
        {
            _autoClock = 0;
            return;
        }

        _autoClock += Game.Time.UnscaledDelta;
        if (_autoClock < AutoDelay(talking.Said))
            return;
        _autoClock = 0;
        Game.AdvanceDialogue(talking);
    }

    // With the menus: a log's panel opens when the wheel turns up over the game, and a log showing scrolls.
    internal void RunBacklogs()
    {
        var logs = Game.Scene.ActiveNow<Backlog>();
        if (logs.Count == 0 && _logsShown.Count == 0 && Game.Input.Scroll <= 0)
            return;
        var top = Game.TopPanel;
        if (top is null && Game.Input.Scroll > 0 && Game.Input.IsMouseInView && !Game.MouseOnUi && OpenLogOnWheel())
            return;

        _logsShown.RemoveWhere(l => !l.Entity!.IsActive || l.Entity.Scene != Game.Scene);
        foreach (var log in logs)
        {
            // Only the one on the panel on top (or on none) takes the keys.
            if (_logsShown.Add(log))
            {
                log.Scroll = 0;
                continue;
            }

            if (top is not null && !PanelSystem.Inside(log.Entity!, top))
                continue;
            ScrollLog(log, PanelOf(log.Entity!));
        }
    }

    private bool OpenLogOnWheel()
    {
        foreach (var log in Game.Scene.AllNow<Backlog>())
        {
            if (!log.OpenOnWheel || log.Entity!.IsActive || PanelOf(log.Entity) is not { } panel || !Game.FlagHolds(panel.NeedsFlag))
                continue;
            if (panel.Entity!.Parent is { IsActive: false })
                continue;
            Game.OpenPanel(panel);
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
        if (Game.Input.Scroll != 0)
        {
            if (Game.Input.Scroll < 0 && log.Scroll <= 0 && log.OpenOnWheel && panel is not null)
            {
                Game.ClosePanel(panel);
                return;
            }

            rows += Game.Input.Scroll * 3;
        }

        // The arrow keys, D-pad and stick a row at a time, held to keep going; Page Up and Down a page; Home and End.
        var up = Game.Input.IsDown(Key.Up) || Game.Input.IsDown(Key.W) || Game.Input.IsDown(GamepadButton.DPadUp) || Game.Input.LeftStick.Y > Game.NavigateStick;
        var down = Game.Input.IsDown(Key.Down) || Game.Input.IsDown(Key.S) || Game.Input.IsDown(GamepadButton.DPadDown) || Game.Input.LeftStick.Y < -Game.NavigateStick;
        if (up != down)
        {
            var sign = up ? 1 : -1;
            if (_logKeyHeld == 0)
                rows += sign;
            _logKeyHeld += Game.Time.UnscaledDelta;
            if (_logKeyHeld > Game.CursorRepeatDelay)
                rows += sign * Game.Time.UnscaledDelta / Game.CursorRepeatEvery;
        }
        else
        {
            _logKeyHeld = 0;
        }

        if (Game.Input.WasPressed(Key.PageUp))
            rows += log.RowsShown;
        if (Game.Input.WasPressed(Key.PageDown))
            rows -= log.RowsShown;
        if (Game.Input.WasPressed(Key.Home))
            rows += log.MostScroll;
        if (Game.Input.WasPressed(Key.End))
            rows -= log.MostScroll;

        // Dragging it (a finger on a touch screen): the lines move with the pointer.
        var area = new Box(Game.Flat(log.Entity!.Transform.WorldPosition), System.Numerics.Vector2.Abs(log.Size) / 2);
        if (Game.Input.WasPressed(MouseButton.Left) && Game.Input.IsMouseInView && area.Contains(Game.Input.MouseWorld))
            _logDragY = Game.Input.MouseWorld.Y;
        if (_logDragY is { } from)
        {
            if (Game.Input.IsDown(MouseButton.Left))
            {
                rows += (from - Game.Input.MouseWorld.Y) / log.RowHeight;
                _logDragY = Game.Input.MouseWorld.Y;
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
    internal void ShowSaveSlots()
    {
        var slots = Game.Scene.ActiveNow<SaveSlot>();
        if (slots.Count == 0)
            return;
        foreach (var slot in slots)
        {
            var saved = string.IsNullOrWhiteSpace(slot.Slot) ? null : Game.SavedGameIn(slot.Slot);
            (slot.HasSave, slot.Picture, slot.PictureAspect) = (saved is not null, saved?.Picture, saved?.PictureAspect ?? 0);
            slot.Description = saved is null ? slot.EmptyText ?? "" : Shorten(saved.Describe(slot.Format ?? ""), slot.PictureSize.X, slot.TextSize);
        }
    }

    // Each line no wider than `width`, and at most three of them.
    private static string Shorten(string text, float width, float size)
    {
        var lines = text.Split('\n').SelectMany(l => LevelSystem.Wrap(l, MathF.Abs(width), size).Split('\n')).ToList();
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
