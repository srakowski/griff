using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Input;
using Griff.Projects;
using Griff.Runtime;

namespace Griff.Editor.Features.Controls;

/// <summary>What the Controls window is waiting for someone to press, if anything.</summary>
public enum ListenFor
{
    Nothing,
    Key,
    GamepadButton,
}

/// <summary>
/// The Controls window: the game's input actions (Jump, Fire, and any the game adds) and the keys, mouse buttons and
/// gamepad buttons that trigger each. Every change is saved in project.griff straight away and reaches a running game.
/// </summary>
public sealed partial class ControlsViewModel : ObservableObject
{
    private static readonly Regex ValidName = new(@"^[A-Za-z][A-Za-z0-9 _]*$");

    private readonly IWorkspace _workspace;
    private readonly PlayMode _play;
    private ActionRow? _selected;
    private ListenFor _listening;
    private string? _status;
    private string? _nameError;

    public ControlsViewModel(IWorkspace workspace, PlayMode play)
    {
        _workspace = workspace;
        _play = play;
        var project = workspace.Project ?? throw new InvalidOperationException("No project is open.");
        var controls = project.Controls;

        foreach (var (name, builtIn) in InputState.BuiltInActions)
            Actions.Add(new ActionRow(this, name, isBuiltIn: true, controls.TryGetValue(name, out var mine) ? mine : builtIn));
        foreach (var (name, bindings) in controls.Where(p => !InputState.BuiltInActions.ContainsKey(p.Key)).OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            Actions.Add(new ActionRow(this, name, isBuiltIn: false, bindings));

        GamepadChoices = Enum.GetValues<GamepadButton>().Select(b => new GamepadChoice(b, () => Add(b))).ToList();
        Selected = Actions[0];
        RefreshConflicts();
    }

    public ObservableCollection<ActionRow> Actions { get; } = [];

    /// <summary>Every gamepad button, for the Add Button list (no controller needed).</summary>
    public IReadOnlyList<GamepadChoice> GamepadChoices { get; }

    public ActionRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                Listening = ListenFor.Nothing;
                NameError = null;
                OnPropertyChanged(nameof(EditName));
                OnPropertyChanged(nameof(PlayerChoice));
                DeleteCommand.NotifyCanExecuteChanged();
                ResetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>The selected action's name, committed when the box loses focus or Enter is pressed.</summary>
    public string EditName
    {
        get => Selected?.Name ?? "";
        set => Rename(value);
    }

    public string? NameError
    {
        get => _nameError;
        private set => SetProperty(ref _nameError, value);
    }

    public ListenFor Listening
    {
        get => _listening;
        set
        {
            if (SetProperty(ref _listening, value))
            {
                OnPropertyChanged(nameof(IsListeningForKey));
                OnPropertyChanged(nameof(IsListeningForPad));
            }
        }
    }

    public bool IsListeningForKey => Listening == ListenFor.Key;

    public bool IsListeningForPad => Listening == ListenFor.GamepadButton;

    public string? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>
    /// The player's own choice for the selected action, made on a controls screen while testing in the editor. It wins
    /// over what's set here until the save data is cleared, so the window says so.
    /// </summary>
    public string? PlayerChoice
    {
        get
        {
            if (Selected is null || _workspace.Project is not { } project)
                return null;
            try
            {
                var path = SaveData.EditorPath(project.RootPath);
                if (!File.Exists(path) || new SaveData(path).GetString(InputState.BindingsSaveKey) is not { } json)
                    return null;
                var saved = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json) ?? [];
                var choice = saved.FirstOrDefault(p => p.Key.Equals(Selected.Name, StringComparison.OrdinalIgnoreCase));
                return choice.Value is null
                    ? null
                    : $"While testing, you rebound {Selected.Name} on a controls screen ({string.Join(", ", choice.Value)}). That choice wins in the editor until Game > Clear Save Data.";
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    [RelayCommand]
    private void AddAction()
    {
        var name = "Action";
        for (var n = 2; Actions.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); n++)
            name = $"Action {n}";
        var row = new ActionRow(this, name, isBuiltIn: false, []);
        Actions.Add(row);
        Selected = row;
        Save($"Added {name}");
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (Selected is not { IsBuiltIn: false } row)
            return;
        var index = Actions.IndexOf(row);
        Actions.Remove(row);
        Selected = Actions[Math.Min(index, Actions.Count - 1)];
        Save($"Deleted {row.Name}");
    }

    private bool CanDelete() => Selected is { IsBuiltIn: false };

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset()
    {
        if (Selected is not { IsBuiltIn: true } row)
            return;
        row.SetBindings(InputState.BuiltInActions[row.Name]);
        Save($"{row.Name} is back to Griff's keys and buttons");
    }

    private bool CanReset() => Selected is { IsBuiltIn: true, IsChanged: true };

    [RelayCommand]
    private void ListenForKey() => Listening = Listening == ListenFor.Key ? ListenFor.Nothing : ListenFor.Key;

    [RelayCommand]
    private void ListenForPad() => Listening = Listening == ListenFor.GamepadButton ? ListenFor.Nothing : ListenFor.GamepadButton;

    [RelayCommand]
    private void Remove(BindingChip chip)
    {
        if (Selected is not { } row)
            return;
        row.SetBindings(row.Bindings.Where(b => b != chip.Binding));
        Save($"{chip.Text} no longer does {row.Name}");
    }

    /// <summary>Adds a key or button to the selected action (from listening, or the gamepad list), and stops listening.</summary>
    public void Add(InputBinding binding)
    {
        Listening = ListenFor.Nothing;
        if (Selected is not { } row)
            return;
        if (row.Bindings.Contains(binding))
        {
            Status = $"{BindingChip.Describe(binding)} already does {row.Name}";
            return;
        }

        row.SetBindings([.. row.Bindings, binding]);
        Save($"{BindingChip.Describe(binding)} now does {row.Name}");
    }

    /// <summary>Listening for a controller button with no controller plugged in.</summary>
    public void NoController()
    {
        Listening = ListenFor.Nothing;
        Status = "No controller is connected. Plug one in, or pick a button with Add Button.";
    }

    // Names are what code and text use ({Jump}, Input.IsDown("Jump")), so they stay simple and unique.
    private void Rename(string value)
    {
        if (Selected is not { } row || value.Trim() == row.Name)
        {
            NameError = null;
            return;
        }

        var name = value.Trim();
        if (row.IsBuiltIn)
            NameError = "Griff's own actions keep their names; add a new action instead.";
        else if (!ValidName.IsMatch(name))
            NameError = "Start with a letter; use letters, digits and spaces.";
        else if (Actions.Any(a => a != row && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            NameError = $"There's already an action called {name}.";
        else
        {
            NameError = null;
            var old = row.Name;
            row.Name = name;
            Save($"Renamed {old} to {name}. Scripts that use \"{old}\" need changing too.");
        }

        OnPropertyChanged(nameof(EditName));
    }

    private void Save(string message)
    {
        RefreshConflicts();
        ResetCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PlayerChoice));
        if (_workspace.Project is not { } project)
            return;

        var controls = Actions.ToDictionary(a => a.Name, a => (IReadOnlyList<InputBinding>)a.Bindings, StringComparer.OrdinalIgnoreCase);
        _ = SaveAsync(project, controls, message);
    }

    private async Task SaveAsync(Project project, Dictionary<string, IReadOnlyList<InputBinding>> controls, string message)
    {
        try
        {
            await project.SetControlsAsync(controls);
            // The running game picks the change up straight away, so it can be tried without stopping.
            if (_play.Game?.Input is InputState input)
            {
                input.UseControls(project.Controls);
                message += " (and in the running game)";
            }

            Status = message;
        }
        catch (IOException ex)
        {
            Status = $"Couldn't save the controls: {ex.Message}";
        }
    }

    // A key that does two things is often a mistake (and sometimes not: X swings and shoots), so it's pointed out.
    private void RefreshConflicts()
    {
        foreach (var row in Actions)
        {
            foreach (var chip in row.Keys.Concat(row.Pads))
            {
                var others = Actions.Where(a => a != row && a.Bindings.Contains(chip.Binding)).Select(a => a.Name).ToList();
                chip.AlsoDoes = others.Count > 0 ? $"Also does {string.Join(" and ", others)}" : null;
            }
        }
    }
}

/// <summary>One input action in the Controls window's list.</summary>
public sealed partial class ActionRow : ObservableObject
{
    private readonly ControlsViewModel _owner;
    private string _name;
    private List<InputBinding> _bindings = [];

    public ActionRow(ControlsViewModel owner, string name, bool isBuiltIn, IEnumerable<InputBinding> bindings)
    {
        _owner = owner;
        _name = name;
        IsBuiltIn = isBuiltIn;
        SetBindings(bindings);
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
                OnPropertyChanged(nameof(Code));
        }
    }

    /// <summary>One of Griff's own actions, which its components use: it can be rebound but not renamed or deleted.</summary>
    public bool IsBuiltIn { get; }

    public IReadOnlyList<InputBinding> Bindings => _bindings;

    public ObservableCollection<BindingChip> Keys { get; } = [];

    public ObservableCollection<BindingChip> Pads { get; } = [];

    /// <summary>"Space, W · A": the list's second line.</summary>
    public string Summary => _bindings.Count == 0 ? "Nothing bound" : string.Join(", ", _bindings.Select(BindingChip.Describe));

    /// <summary>A built-in action whose keys and buttons differ from Griff's.</summary>
    public bool IsChanged => IsBuiltIn && !InputState.BuiltInActions[Name].SequenceEqual(_bindings);

    /// <summary>What uses the action, for Griff's own; for the game's, how scripts read it.</summary>
    public string UsedBy => Name.ToLowerInvariant() switch
    {
        _ when !IsBuiltIn => "Your own action: scripts read it, button prompts show it ({" + Name + "}), and Rebind buttons can change it.",
        "jump" => "Platformer Movement jumps with it.",
        "fire" => "A Shooter whose Action is Fire shoots while it's held.",
        "dash" => "Platformer and Top-down Movement dash with it.",
        "attack" => "An Attack (a sword) swings with it.",
        "interact" => "Doors, levers, signs and people to talk to wait for it.",
        "pause" => "Panels that Open On Pause (pause menus) open and close with it.",
        "endturn" => "A Turn Order ends a person's turn with it.",
        _ => "Griff's components use it.",
    };

    public string Code => $"Input.WasPressed(\"{Name}\")";

    public void SetBindings(IEnumerable<InputBinding> bindings)
    {
        _bindings = bindings.Distinct().ToList();
        Keys.Clear();
        Pads.Clear();
        foreach (var binding in _bindings)
            (binding.Gamepad is null ? Keys : Pads).Add(new BindingChip(binding, _owner.RemoveCommand));
        OnPropertyChanged(nameof(Bindings));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsChanged));
    }
}

/// <summary>A key or button on an action, with a button to take it off.</summary>
public sealed class BindingChip(InputBinding binding, IRelayCommand<BindingChip> remove) : ObservableObject
{
    private string? _alsoDoes;

    public InputBinding Binding { get; } = binding;

    public string Text => Describe(Binding);

    public IRelayCommand<BindingChip> RemoveCommand { get; } = remove;

    /// <summary>"Also does Attack" when another action has it too.</summary>
    public string? AlsoDoes
    {
        get => _alsoDoes;
        set
        {
            if (SetProperty(ref _alsoDoes, value))
                OnPropertyChanged(nameof(Tip));
        }
    }

    public string Tip => AlsoDoes is null ? $"{Text}. Click × to remove it." : $"{Text}. {AlsoDoes}. Click × to remove it.";

    /// <summary>"Left Shift", "Left Click", "A", "RB": a key's side is shown, unlike in-game prompts.</summary>
    public static string Describe(InputBinding binding) => binding.Key switch
    {
        Key.LeftShift => "Left Shift",
        Key.RightShift => "Right Shift",
        Key.LeftControl => "Left Ctrl",
        Key.RightControl => "Right Ctrl",
        Key.LeftAlt => "Left Alt",
        Key.RightAlt => "Right Alt",
        _ => InputPrompts.Name(binding),
    };
}

/// <summary>A gamepad button in the Add Button list.</summary>
public sealed class GamepadChoice(GamepadButton button, Action add)
{
    public GamepadButton Button { get; } = button;

    public string Label { get; } = button switch
    {
        GamepadButton.LeftShoulder => "LB (left bumper)",
        GamepadButton.RightShoulder => "RB (right bumper)",
        GamepadButton.LeftTrigger => "LT (left trigger)",
        GamepadButton.RightTrigger => "RT (right trigger)",
        GamepadButton.LeftStick => "LS (press the left stick)",
        GamepadButton.RightStick => "RS (press the right stick)",
        GamepadButton.Back => "Back (View)",
        GamepadButton.Start => "Start (Menu)",
        _ => InputPrompts.Name(button),
    };

    public RelayCommand Command { get; } = new(add);
}
