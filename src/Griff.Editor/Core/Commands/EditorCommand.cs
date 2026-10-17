using System.Globalization;
using System.Reflection;
using System.Windows.Input;

namespace Griff.Editor.Core.Commands;

/// <summary>
/// Base class for every user-invokable action in the editor. Modules register commands in DI;
/// the shell discovers them and builds menus and key bindings from their metadata.
/// </summary>
public abstract class EditorCommand : ICommand
{
    private bool _isExecuting;
    private string? _category;

    /// <summary>Stable identifier, e.g. "File.Save". Used for lookup, keymaps and a future command palette.</summary>
    public abstract string Id { get; }

    /// <summary>
    /// The type whose /// summary says what the command does, for agents (it's their tool's description): the command's
    /// own, unless it stands for something else, as an Entity menu preset's command does for the preset.
    /// </summary>
    public virtual Type DescribedBy => GetType();

    /// <summary>
    /// The category it's listed under when it makes something (the Entity menu's submenus and the Add Entity palette, e.g.
    /// "Characters", or a genre module's name), from a [Category] on its <see cref="DescribedBy"/> type, the way components
    /// get theirs; null for an ordinary command.
    /// </summary>
    public string? Category => (_category ??= DescribedBy.GetCustomAttribute<Griff.Scenes.CategoryAttribute>(inherit: false)?.Name ?? "") is { Length: > 0 } name ? name : null;

    /// <summary>Raised after it has run (from a menu, a palette, a key or an agent) without throwing.</summary>
    public event EventHandler? Executed;

    /// <summary>Display text. Use '_' to mark the access key.</summary>
    public abstract string Text { get; }

    /// <summary><see cref="Text"/> without access-key markers, for tooltips and palettes.</summary>
    public string DisplayText => Text.Replace("_", "");

    /// <summary>Glyph from <see cref="Themes.Icons"/>. Required for toolbar commands.</summary>
    public virtual string? Icon => null;

    public virtual KeyGesture? Gesture => null;

    /// <summary>Another shortcut that runs it too (Ctrl+Shift+Z for Redo, beside Ctrl+Y), or null.</summary>
    public virtual KeyGesture? AlternateGesture => null;

    public virtual string? GestureText => Gesture?.GetDisplayStringForCulture(CultureInfo.CurrentUICulture);

    public string ToolTipText => GestureText is null ? DisplayText : $"{DisplayText} ({GestureText})";

    /// <summary>Where the command appears in the main menu, or null to keep it out of the menu.</summary>
    public virtual MenuPlacement? Placement => null;

    /// <summary>Where the command appears on the main toolbar, or null to keep it off the toolbar.</summary>
    public virtual ToolbarPlacement? Toolbar => null;

    /// <summary>
    /// For a command that's on a right-click menu rather than the main menu (a component's, in the Inspector): which menu
    /// (said to agents in place of a place in the main menu) and where on it. The view that shows the menu builds it from
    /// the commands placed there, giving each what it was opened on. Null for one that isn't.
    /// </summary>
    public virtual ContextMenuPlacement? ContextPlacement => null;

    /// <summary>What its menu item says for <paramref name="target"/> ("Paste Sprite Values"); its <see cref="Text"/> unless it says more.</summary>
    public virtual string TextFor(object? target) => Text;

    /// <summary>Its menu item's tooltip on <paramref name="target"/> when it can run on it, or null for none.</summary>
    public virtual string? TipFor(object? target) => null;

    /// <summary>Whether its right-click menu shows it for <paramref name="target"/> (Add to All only on a component only some have).</summary>
    public virtual bool IsOnMenuFor(object? target) => true;

    /// <summary>
    /// For a command that acts on something it's given rather than on what's selected or active (a component's menu gives it
    /// its component): what that is, and how an agent names one. Null for a command that takes nothing.
    /// </summary>
    public virtual CommandTarget? Target => null;

    /// <summary>For an on/off setting: whether it's on, shown as a tick by its menu item. Null for an ordinary command.</summary>
    public virtual bool? IsChecked => null;

    /// <summary>
    /// For a command that picks one of several things (a saved layout, say): what it can pick now, shown as a submenu
    /// of its menu item. Null for an ordinary command. Run without a pick, it asks which.
    /// </summary>
    public virtual IReadOnlyList<string>? Choices => null;

    /// <summary>For a command that picks a setting: the one of its <see cref="Choices"/> in effect now, ticked in its submenu. Null for none.</summary>
    public virtual string? Chosen => null;

    /// <summary>
    /// What the command came to the last time it ran, said to an agent after it finishes (a simulation's results, say); null
    /// for nothing more than that it ran. A command sets it as it runs.
    /// </summary>
    public string? Outcome { get; protected set; }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute() => CanExecute(null);

    /// <summary>Whether it can run now on <paramref name="target"/> (null for what's selected or active).</summary>
    public bool CanExecute(object? target) => !_isExecuting && CanExecuteCore(target);

    public Task ExecuteAsync() => ExecuteAsync(null);

    /// <summary>Runs it on <paramref name="target"/> (null for what's selected or active), as a menu item giving it one does.</summary>
    public Task ExecuteAsync(object? target) => RunAsync(() => ExecuteCoreAsync(target), target);

    /// <summary>Runs it with one of its <see cref="Choices"/> picked, as clicking that in its submenu does.</summary>
    public Task ChooseAsync(string choice) => RunAsync(() => ChooseCoreAsync(choice), null);

    /// <summary>Why it can't run now on <paramref name="target"/>, said to an agent that tried; null when it can, or for nothing more to say.</summary>
    public virtual string? WhyNot(object? target) => null;

    protected virtual bool CanExecuteCore() => true;

    /// <summary>Whether it can run on <paramref name="target"/>; a command that takes nothing asks <see cref="CanExecuteCore()"/>.</summary>
    protected virtual bool CanExecuteCore(object? target) => CanExecuteCore();

    protected abstract Task ExecuteCoreAsync();

    /// <summary>Runs it on <paramref name="target"/>; a command that takes nothing runs <see cref="ExecuteCoreAsync()"/>.</summary>
    protected virtual Task ExecuteCoreAsync(object? target) => ExecuteCoreAsync();

    protected virtual Task ChooseCoreAsync(string choice) => throw new NotSupportedException($"{DisplayText} has nothing to pick.");

    private async Task RunAsync(Func<Task> run, object? target)
    {
        if (!CanExecute(target))
            return;

        // Disable the command while it runs so a slow save can't be triggered twice.
        _isExecuting = true;
        Outcome = null;
        var ran = false;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await run();
            ran = true;
        }
        finally
        {
            _isExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }

        if (ran)
            Executed?.Invoke(this, EventArgs.Empty);
    }

    // A button or menu item's CommandParameter is what it's run on (a component's menu gives its component).
    bool ICommand.CanExecute(object? parameter) => CanExecute(parameter);

    // Exceptions surface through Application.DispatcherUnhandledException, which reports them centrally.
    async void ICommand.Execute(object? parameter) => await ExecuteAsync(parameter);
}

/// <summary>
/// Where a command goes on a right-click menu: the menu, as said to agents ("a component's menu in the Inspector"), and its
/// group (separated from the next) and order in it.
/// </summary>
public sealed record ContextMenuPlacement(string Menu, int Group, int Order);

/// <summary>
/// What a command acts on when it's given one (see <see cref="EditorCommand.Target"/>): the argument agents name it in
/// ("component"), what to put there, and how the command finds it from that. An <see cref="Optional"/> one may be left out.
/// </summary>
public sealed record CommandTarget(string Argument, string Description, Func<string, object> Find)
{
    /// <summary>True when the command also runs without one, as it does from its menu item (an agent may leave the argument out).</summary>
    public bool Optional { get; init; }
}
