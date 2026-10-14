using System.Globalization;
using System.Windows.Input;

namespace Griff.Editor.Core.Commands;

/// <summary>
/// Base class for every user-invokable action in the editor. Modules register commands in DI;
/// the shell discovers them and builds menus and key bindings from their metadata.
/// </summary>
public abstract class EditorCommand : ICommand
{
    private bool _isExecuting;

    /// <summary>Stable identifier, e.g. "File.Save". Used for lookup, keymaps and a future command palette.</summary>
    public abstract string Id { get; }

    /// <summary>
    /// The type whose /// summary says what the command does, for agents (it's their tool's description): the command's
    /// own, unless it stands for something else, as an Entity menu preset's command does for the preset.
    /// </summary>
    public virtual Type DescribedBy => GetType();

    /// <summary>Display text. Use '_' to mark the access key.</summary>
    public abstract string Text { get; }

    /// <summary><see cref="Text"/> without access-key markers, for tooltips and palettes.</summary>
    public string DisplayText => Text.Replace("_", "");

    /// <summary>Glyph from <see cref="Themes.Icons"/>. Required for toolbar commands.</summary>
    public virtual string? Icon => null;

    public virtual KeyGesture? Gesture => null;

    public virtual string? GestureText => Gesture?.GetDisplayStringForCulture(CultureInfo.CurrentUICulture);

    public string ToolTipText => GestureText is null ? DisplayText : $"{DisplayText} ({GestureText})";

    /// <summary>Where the command appears in the main menu, or null to keep it out of the menu.</summary>
    public virtual MenuPlacement? Placement => null;

    /// <summary>Where the command appears on the main toolbar, or null to keep it off the toolbar.</summary>
    public virtual ToolbarPlacement? Toolbar => null;

    /// <summary>For an on/off setting: whether it's on, shown as a tick by its menu item. Null for an ordinary command.</summary>
    public virtual bool? IsChecked => null;

    /// <summary>
    /// For a command that picks one of several things (a saved layout, say): what it can pick now, shown as a submenu
    /// of its menu item. Null for an ordinary command. Run without a pick, it asks which.
    /// </summary>
    public virtual IReadOnlyList<string>? Choices => null;

    /// <summary>For a command that picks a setting: the one of its <see cref="Choices"/> in effect now, ticked in its submenu. Null for none.</summary>
    public virtual string? Chosen => null;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute() => !_isExecuting && CanExecuteCore();

    public Task ExecuteAsync() => RunAsync(ExecuteCoreAsync);

    /// <summary>Runs it with one of its <see cref="Choices"/> picked, as clicking that in its submenu does.</summary>
    public Task ChooseAsync(string choice) => RunAsync(() => ChooseCoreAsync(choice));

    protected virtual bool CanExecuteCore() => true;

    protected abstract Task ExecuteCoreAsync();

    protected virtual Task ChooseCoreAsync(string choice) => throw new NotSupportedException($"{DisplayText} has nothing to pick.");

    private async Task RunAsync(Func<Task> run)
    {
        if (!CanExecute())
            return;

        // Disable the command while it runs so a slow save can't be triggered twice.
        _isExecuting = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await run();
        }
        finally
        {
            _isExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    bool ICommand.CanExecute(object? parameter) => CanExecute();

    // Exceptions surface through Application.DispatcherUnhandledException, which reports them centrally.
    async void ICommand.Execute(object? parameter) => await ExecuteAsync();
}
