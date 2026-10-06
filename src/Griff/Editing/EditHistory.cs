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

    /// <summary>Raised after an edit is executed, undone or redone.</summary>
    public event EventHandler<EditEventArgs>? Changed;

    public bool CanUndo => _undo.Count > 0 && _groups.Count == 0;

    public bool CanRedo => _redo.Count > 0 && _groups.Count == 0;

    public string? UndoDescription => _undo.Count > 0 ? _undo[^1].Edit.Description : null;

    public string? RedoDescription => _redo.Count > 0 ? _redo[^1].Edit.Description : null;

    /// <summary>True when the current state differs from the last <see cref="MarkSaved"/>.</summary>
    public bool IsDirty => CurrentEntryId != _savedEntryId;

    private long CurrentEntryId => _undo.Count > 0 ? _undo[^1].Id : 0;

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
    /// Groups every edit executed until the returned scope is disposed into one undo step.
    /// Groups can nest; only the outermost becomes a history entry.
    /// </summary>
    public IDisposable BeginGroup(string description)
    {
        _groups.Push([]);
        return new GroupScope(this, description);
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

    private sealed class GroupScope(EditHistory history, string description) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            history.EndGroup(description);
        }
    }
}
