using Griff.Editor.Core.Settings;

namespace Griff.Editor.Core.Commands;

/// <summary>
/// Every registered command, indexed by id, and which of those that make things (the ones with a
/// <see cref="EditorCommand.Category"/>) were used lately, kept in the editor settings.
/// </summary>
public sealed class CommandRegistry
{
    /// <summary>How many recently used things the Add Entity palette shows at its top.</summary>
    public const int MaxRecent = 5;

    private readonly Dictionary<string, EditorCommand> _commands = new(StringComparer.Ordinal);
    private readonly ISettingsStore _settings;

    public CommandRegistry(IEnumerable<EditorCommand> commands, ISettingsStore settings)
    {
        _settings = settings;
        foreach (var command in commands)
        {
            if (!_commands.TryAdd(command.Id, command))
                throw new InvalidOperationException($"Duplicate command id '{command.Id}'.");
        }

        Categorized = CommandCategories.Group(_commands.Values);
        foreach (var command in Categorized.SelectMany(g => g))
            command.Executed += (_, _) => Remember(command);
    }

    public IReadOnlyCollection<EditorCommand> All => _commands.Values;

    /// <summary>The commands that make things, by category in the order menus list them (see <see cref="CommandCategories"/>).</summary>
    public IReadOnlyList<IGrouping<string, EditorCommand>> Categorized { get; }

    /// <summary>The commands that make things used most recently, most recent first (at most <see cref="MaxRecent"/>).</summary>
    public IReadOnlyList<EditorCommand> Recent =>
        _settings.Settings.RecentEntities.Select(id => _commands.GetValueOrDefault(id)).OfType<EditorCommand>()
            .Where(c => c.Category is not null).Take(MaxRecent).ToList();

    public EditorCommand Get(string id) =>
        _commands.TryGetValue(id, out var command)
            ? command
            : throw new KeyNotFoundException($"No command registered with id '{id}'.");

    // Puts a command first among the recently used, in the settings.
    private void Remember(EditorCommand command)
    {
        var recent = _settings.Settings.RecentEntities;
        if (recent.Count > 0 && recent[0] == command.Id)
            return;
        recent.Remove(command.Id);
        recent.Insert(0, command.Id);
        if (recent.Count > MaxRecent * 2)
            recent.RemoveRange(MaxRecent * 2, recent.Count - MaxRecent * 2);
        _settings.Save();
    }
}
