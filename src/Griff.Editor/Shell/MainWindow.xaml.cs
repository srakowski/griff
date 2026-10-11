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
        Features.Prefabs.PrefabTabs prefabTabs)
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
        prefabTabs.Attach(DocumentTabs, SceneTab, sceneView);
        scriptTabs.Attach(DocumentTabs);

        menuBuilder.Populate(MainMenu);
        toolbarBuilder.Populate(Toolbar);
        foreach (var command in commands.All)
        {
            if (command.Gesture is { } gesture)
                InputBindings.Add(new KeyBinding(command, gesture));
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
