using System.Collections;

namespace Griff.Scenes;

/// <summary>
/// One mistake in a game's data (a prefab that isn't there, a name that finds nothing, a row that doesn't read), said
/// once: what it is, and the entity and component it was found on when there is one, so the Inspector can flag the
/// component and an agent can find it.
/// </summary>
/// <param name="Message">What's wrong, in words a game's author reads (it names the component and entity too).</param>
/// <param name="EntityName">The name of the entity it was found on, or null.</param>
/// <param name="ComponentType">The type name of the component it was found on, or null.</param>
public sealed record Problem(string Message, string? EntityName = null, string? ComponentType = null)
{
    /// <summary>True when it was found on <paramref name="component"/>: the entity's name and the component's type name match.</summary>
    public bool Concerns(Component component) =>
        EntityName is not null && ComponentType is not null && component.Entity is { } entity &&
        string.Equals(entity.Name, EntityName, StringComparison.Ordinal) && string.Equals(component.GetType().Name, ComponentType, StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Message;
}

/// <summary>
/// The one place a mistake in a game's data goes (see <c>docs/architecture.md</c>): a game, a loader, the scene serializer
/// and a simulation all report here, and the editor's Output, the Inspector and the agent tools read from here. The rule it
/// serves: a mistake in a game's data warns, once, and the game goes on; a mistake in code throws. It is also an
/// <see cref="ICollection{T}"/> of the lines said, so the loaders' <c>warnings</c> parameters take it.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710", Justification = "It is the log of problems, handed to loaders as their ICollection of warning lines.")]
public sealed class ProblemLog : ICollection<string>
{
    private readonly List<Problem> _problems = [];
    private readonly HashSet<string> _said = [];

    /// <summary>Raised for each new problem (never twice for the same words), on the thread that reported it.</summary>
    public event Action<Problem>? Reported;

    /// <summary>Every problem reported, in the order they came.</summary>
    public IReadOnlyList<Problem> Problems
    {
        get
        {
            lock (_said)
                return [.. _problems];
        }
    }

    /// <summary>The words of every problem reported.</summary>
    public IReadOnlyList<string> Lines => [.. Problems.Select(p => p.Message)];

    /// <summary>Reports a problem, naming <paramref name="component"/> (and its entity) when it is on one; said once.</summary>
    public void Report(string message, Component? component = null) =>
        Report(new Problem(message, component?.Entity?.Name, component?.GetType().Name));

    /// <summary>Reports <paramref name="problem"/>, unless these words were said already.</summary>
    public void Report(Problem problem)
    {
        lock (_said)
        {
            if (!_said.Add(problem.Message))
                return;
            _problems.Add(problem);
        }

        Reported?.Invoke(problem);
    }

    /// <summary>Counts <paramref name="messages"/> as said, without telling anyone: a host that already showed them (the loader's).</summary>
    public void MarkSaid(IEnumerable<string> messages)
    {
        lock (_said)
            _said.UnionWith(messages);
    }

    /// <summary>The problems found on <paramref name="component"/>.</summary>
    public IEnumerable<Problem> On(Component component) => Problems.Where(p => p.Concerns(component));

    /// <summary>Forgets everything said, for a run that starts over.</summary>
    public void Clear()
    {
        lock (_said)
        {
            _problems.Clear();
            _said.Clear();
        }
    }

    /// <inheritdoc />
    public int Count => Problems.Count;

    bool ICollection<string>.IsReadOnly => false;

    void ICollection<string>.Add(string item) => Report(item);

    bool ICollection<string>.Contains(string item)
    {
        lock (_said)
            return _said.Contains(item);
    }

    void ICollection<string>.CopyTo(string[] array, int arrayIndex) => Lines.ToList().CopyTo(array, arrayIndex);

    bool ICollection<string>.Remove(string item) => throw new NotSupportedException("Problems that were said stay said.");

    /// <inheritdoc />
    public IEnumerator<string> GetEnumerator() => Lines.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
