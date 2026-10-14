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
    private readonly IWorkspace _workspace;
    private bool _closeConfirmed;

    public MainWindow(
        MainWindowViewModel viewModel,
        IWorkspace workspace,
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
        Features.Debugging.Debugger debugger)
    {
        InitializeComponent();
        DataContext = viewModel;
        _workspace = workspace;
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
        foreach (var shared in commands.All.Where(c => c.Gesture is not null).GroupBy(c => (c.Gesture!.Key, c.Gesture.Modifiers)))
        {
            var list = shared.ToList();
            InputBindings.Add(new KeyBinding(list.Count == 1 ? list[0] : new FirstThatCanRun(list), list[0].Gesture!));
        }

        panelHost.Attach(this, DockRoot, DocumentArea);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || e.Cancel)
            return;

        // Closing can't await, so cancel this attempt and close again once the user has answered.
        e.Cancel = true;
        if (await _workspace.ConfirmCloseProjectAsync())
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
