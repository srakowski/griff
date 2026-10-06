namespace Griff.Editor.Core.Commands;

/// <summary>Every registered command, indexed by id.</summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, EditorCommand> _commands = new(StringComparer.Ordinal);

    public CommandRegistry(IEnumerable<EditorCommand> commands)
    {
        foreach (var command in commands)
        {
            if (!_commands.TryAdd(command.Id, command))
                throw new InvalidOperationException($"Duplicate command id '{command.Id}'.");
        }
    }

    public IReadOnlyCollection<EditorCommand> All => _commands.Values;

    public EditorCommand Get(string id) =>
        _commands.TryGetValue(id, out var command)
            ? command
            : throw new KeyNotFoundException($"No command registered with id '{id}'.");
}
