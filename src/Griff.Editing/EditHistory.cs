namespace Griff.Editing;

public enum EditAction
{
    Execute,
    Undo,
    Redo,
}

public sealed class EditEventArgs(IEdit edit, EditAction action) : EventArgs
{
    public IEdit Edit { get; } = edit;

    public EditAction Action { get; } = action;
}

/// <summary>
/// Undo/redo stacks plus save tracking. <see cref="IsDirty"/> follows the history, so undoing back
/// to the saved state makes the document clean again.
/// </summary>
public sealed class EditHistory
{
    private readonly List<Entry> _undo = [];
    private readonly List<Entry> _redo = [];
    private readonly Stack<List<IEdit>> _groups = [];
    private long _nextEntryId = 1;
    private long _savedEntryId;
    private bool _sealed = true;

    /// <summary>
    /// How many steps it keeps to undo, the oldest let go past it (the Sprite Painter keeps 200 strokes); no limit unless
    /// set.
    /// </summary>
    public int Limit { get; init; } = int.MaxValue;

    /// <summary>Raised after an edit is executed, undone or redone.</summary>
    public event EventHandler<EditEventArgs>? Changed;

    public bool CanUndo => _undo.Count > 0 && _groups.Count == 0;

    public bool CanRedo => _redo.Count > 0 && _groups.Count == 0;

    public string? UndoDescription => _undo.Count > 0 ? _undo[^1].Edit.Description : null;

    public string? RedoDescription => _redo.Count > 0 ? _redo[^1].Edit.Description : null;

    /// <summary>True when the current state differs from the last <see cref="MarkSaved"/>.</summary>
    public bool IsDirty => CurrentEntryId != _savedEntryId;

    // The last edit that changed the document (one saved as it's made, like a project setting, leaves it as it was).
    private long CurrentEntryId
    {
        get
        {
            for (var i = _undo.Count - 1; i >= 0; i--)
            {
                if (_undo[i].Edit.ChangesDocument)
                    return _undo[i].Id;
            }

            return 0;
        }
    }

    /// <summary>Applies <paramref name="edit"/> and records it, merging with the previous edit when possible.</summary>
    public void Execute(IEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        edit.Apply();

        if (_groups.TryPeek(out var group))
        {
            if (group.Count == 0 || !group[^1].TryMerge(edit))
                group.Add(edit);
            return;
        }

        Record(edit);
        Changed?.Invoke(this, new EditEventArgs(edit, EditAction.Execute));
    }

    /// <summary>
    /// Records <paramref name="edit"/>, a change that has already happened (its file changed outside the editor), as a step
    /// to undo, without applying it again.
    /// </summary>
    public void RecordDone(IEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        EnsureNotGrouping();
        _sealed = true;
        Record(edit);
        _sealed = true;
        Changed?.Invoke(this, new EditEventArgs(edit, EditAction.Execute));
    }

    /// <summary>
    /// Groups every edit executed until the returned scope is disposed into one undo step.
    /// Groups can nest; only the outermost becomes a history entry.
    /// </summary>
    public EditGroup BeginGroup(string description)
    {
        List<IEdit> edits = [];
        _groups.Push(edits);
        return new EditGroup(this, edits, description);
    }

    /// <summary>Stops the next edit merging into the current one, e.g. at the end of a drag.</summary>
    public void Seal() => _sealed = true;

    public void Undo()
    {
        EnsureNotGrouping();
        if (_undo.Count == 0)
            return;

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        entry.Edit.Revert();
        _redo.Add(entry);
        _sealed = true;
        Changed?.Invoke(this, new EditEventArgs(entry.Edit, EditAction.Undo));
    }

    public void Redo()
    {
        EnsureNotGrouping();
        if (_redo.Count == 0)
            return;

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        entry.Edit.Apply();
        _undo.Add(entry);
        _sealed = true;
        Changed?.Invoke(this, new EditEventArgs(entry.Edit, EditAction.Redo));
    }

    /// <summary>
    /// Points recorded edits at rebuilt components after a script reload. Edits that can't be remapped
    /// (e.g. the property was deleted) are dropped from the history.
    /// </summary>
    public void RemapComponents(IReadOnlyDictionary<Scenes.Component, Scenes.Component> replaced, Scenes.Serialization.ComponentRegistry registry)
    {
        if (replaced.Count == 0)
            return;

        RemapStack(_undo);
        RemapStack(_redo);
        _sealed = true;

        void RemapStack(List<Entry> stack)
        {
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                if (Remap(stack[i].Edit) is { } edit)
                    stack[i] = stack[i] with { Edit = edit };
                else
                    stack.RemoveAt(i);
            }
        }

        IEdit? Remap(IEdit edit) => edit switch
        {
            IComponentEdit component => component.Remap(replaced, registry),
            CompositeEdit composite => composite.Edits.Select(Remap).OfType<IEdit>().ToList() is { Count: > 0 } edits
                ? new CompositeEdit(composite.Description, edits)
                : null,
            _ => edit,
        };
    }

    public void MarkSaved()
    {
        _savedEntryId = CurrentEntryId;
        _sealed = true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _savedEntryId = 0;
        _sealed = true;
    }

    private void Record(IEdit edit)
    {
        _redo.Clear();

        // Merging into the saved entry would hide the change from IsDirty.
        if (!_sealed && _undo.Count > 0 && _undo[^1].Id != _savedEntryId && _undo[^1].Edit.TryMerge(edit))
            return;

        _undo.Add(new Entry(_nextEntryId++, edit));
        if (_undo.Count > Limit)
            _undo.RemoveAt(0);
        _sealed = false;
    }

    private void EndGroup(string description)
    {
        var edits = _groups.Pop();
        if (edits.Count == 0)
            return;

        var edit = edits.Count == 1 ? edits[0] : new CompositeEdit(description, edits);
        if (_groups.TryPeek(out var outer))
        {
            outer.Add(edit);
            return;
        }

        _sealed = true;
        Record(edit);
        _sealed = true;
        Changed?.Invoke(this, new EditEventArgs(edit, EditAction.Execute));
    }

    private void EnsureNotGrouping()
    {
        if (_groups.Count > 0)
            throw new InvalidOperationException("Can't undo or redo while an edit group is open.");
    }

    private sealed record Entry(long Id, IEdit Edit);

    /// <summary>An open group of edits (see <see cref="BeginGroup"/>): disposing it makes them one undo step.</summary>
    public sealed class EditGroup : IDisposable
    {
        private readonly EditHistory _history;
        private readonly List<IEdit> _edits;
        private readonly string _description;
        private bool _disposed;

        internal EditGroup(EditHistory history, List<IEdit> edits, string description)
        {
            _history = history;
            _edits = edits;
            _description = description;
        }

        /// <summary>
        /// Takes back every edit made in the group so far, newest first, so disposing it records none of them: how a
        /// change that fails partway, or one only tried to see what it would do, leaves the document as it was. Call it
        /// with no group open inside this one.
        /// </summary>
        public void Discard()
        {
            for (var i = _edits.Count - 1; i >= 0; i--)
                _edits[i].Revert();
            _edits.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _history.EndGroup(_description);
        }
    }
}
