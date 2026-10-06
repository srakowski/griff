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

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute() => !_isExecuting && CanExecuteCore();

    public async Task ExecuteAsync()
    {
        if (!CanExecute())
            return;

        // Disable the command while it runs so a slow save can't be triggered twice.
        _isExecuting = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await ExecuteCoreAsync();
        }
        finally
        {
            _isExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    protected virtual bool CanExecuteCore() => true;

    protected abstract Task ExecuteCoreAsync();

    bool ICommand.CanExecute(object? parameter) => CanExecute();

    // Exceptions surface through Application.DispatcherUnhandledException, which reports them centrally.
    async void ICommand.Execute(object? parameter) => await ExecuteAsync();
}
