using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.SceneView;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Shell;

public partial class MainWindow : ChromeWindow
{
    private readonly IProjectSession _projectSession;
    private bool _closeConfirmed;

    public MainWindow(
        MainWindowViewModel viewModel,
        IProjectSession projectSession,
        SceneViewControl sceneView,
        StartPage startPage,
        PlayMode play,
        TimelineBar timeline,
        CommandRegistry commands,
        MenuBuilder menuBuilder,
        ToolbarBuilder toolbarBuilder,
        PanelHost panelHost,
        Features.Scripts.ScriptTabs scriptTabs,
        Features.Prefabs.PrefabTabs prefabTabs,
        Features.Debugging.Debugger debugger,
        PlayShortcuts playShortcuts,
        FullscreenPlay fullscreen,
        Core.EditorWindow editorWindow)
    {
        InitializeComponent();
        editorWindow.Attach(this, DocumentTabs, SceneTab, GameTab, CloseConfirmed);
        DataContext = viewModel;
        _projectSession = projectSession;
        SceneTab.Content = sceneView;
        var gameView = new DockPanel();
        DockPanel.SetDock(timeline, Dock.Top);
        gameView.Children.Add(timeline);
        gameView.Children.Add(play.Surface);
        GameTab.Content = gameView;
        StartHost.Content = startPage;
        play.Started += (_, _) => DocumentTabs.SelectedItem = GameTab;
        play.Stopped += (_, _) => DocumentTabs.SelectedItem = SceneTab;
        // A breakpoint shows its script; carrying on (after any steps) goes back to the game, if that's what was showing.
        var gameBeforeBreak = false;
        void BackToGame()
        {
            if (gameBeforeBreak && play.IsPlaying && !play.IsPaused && !debugger.IsPaused)
            {
                gameBeforeBreak = false;
                DocumentTabs.SelectedItem = GameTab;
            }
        }

        debugger.PausedChanged += (_, _) =>
        {
            if (debugger.IsPaused && !debugger.StoppedByStep)
                gameBeforeBreak = DocumentTabs.SelectedItem == GameTab;
            BackToGame();
        };
        play.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlayMode.IsPaused))
                BackToGame();
        };
        prefabTabs.Attach(DocumentTabs, SceneTab, sceneView);
        scriptTabs.Attach(DocumentTabs);

        menuBuilder.Populate(MainMenu);
        toolbarBuilder.Populate(Toolbar);
        // Commands can share a shortcut when only one can run at a time (F10 steps a frame, or a line at a breakpoint).
        foreach (var shared in Shortcuts(commands.All).GroupBy(s => (s.Gesture.Key, s.Gesture.Modifiers)))
        {
            var list = shared.Select(s => s.Command).ToList();
            InputBindings.Add(new KeyBinding(list.Count == 1 ? list[0] : new FirstThatCanRun(list), shared.First().Gesture));
        }

        // Over a running game the Game menu's shortcuts come first (the game takes the keys): attached now, before any game
        // listens to the window. FullscreenPlay, made with the window, moves the Game tab's view to a window filling the
        // screen and back as PlayMode.IsFullscreen says, and attaches them there too.
        playShortcuts.Attach(this);
        _ = fullscreen;

        panelHost.Attach(this, DockRoot, DocumentArea, DocumentTabs);
    }

    /// <summary>Every command's shortcuts: its gesture and any other it has (Redo's Ctrl+Shift+Z).</summary>
    public static IEnumerable<(KeyGesture Gesture, EditorCommand Command)> Shortcuts(IEnumerable<EditorCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command.Gesture is { } gesture)
                yield return (gesture, command);
            if (command.AlternateGesture is { } other)
                yield return (other, command);
        }
    }

    /// <summary>Closes the window for a caller that has already asked about unsaved changes.</summary>
    public void CloseConfirmed()
    {
        _closeConfirmed = true;
        Close();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || e.Cancel)
            return;

        // Closing can't await, so cancel this attempt and close again once the user has answered.
        e.Cancel = true;
        if (await _projectSession.ConfirmCloseProjectAsync())
        {
            _closeConfirmed = true;
            _ = Dispatcher.InvokeAsync(Close);
        }
    }
}

// The commands sharing a shortcut: it runs the first of them that can run now.
internal sealed class FirstThatCanRun(IReadOnlyList<EditorCommand> commands) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => commands.Any(c => ((ICommand)c).CanExecute(parameter));

    public void Execute(object? parameter) => ((ICommand?)commands.FirstOrDefault(c => ((ICommand)c).CanExecute(parameter)))?.Execute(parameter);
}
