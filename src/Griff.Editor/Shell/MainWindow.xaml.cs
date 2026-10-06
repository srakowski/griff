using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Documents;
using Griff.Editor.Core.Panels;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Shell;

public partial class MainWindow : ChromeWindow
{
    private readonly IDocumentService _documents;
    private bool _closeConfirmed;

    public MainWindow(
        MainWindowViewModel viewModel,
        IDocumentService documents,
        CommandRegistry commands,
        MenuBuilder menuBuilder,
        ToolbarBuilder toolbarBuilder,
        PanelHost panelHost)
    {
        InitializeComponent();
        DataContext = viewModel;
        _documents = documents;

        menuBuilder.Populate(MainMenu);
        toolbarBuilder.Populate(Toolbar);
        foreach (var command in commands.All)
        {
            if (command.Gesture is { } gesture)
                InputBindings.Add(new KeyBinding(command, gesture));
        }

        panelHost.Populate(new Dictionary<PanelZone, TabControl>
        {
            [PanelZone.Left] = LeftZone,
            [PanelZone.Right] = RightZone,
            [PanelZone.Bottom] = BottomZone,
        });
        CollapseEmptyZones();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || e.Cancel)
            return;

        // Closing can't await, so cancel this attempt and close again once the user has answered.
        e.Cancel = true;
        if (await _documents.ConfirmCloseAsync())
        {
            _closeConfirmed = true;
            _ = Dispatcher.InvokeAsync(Close);
        }
    }

    private void CollapseEmptyZones()
    {
        if (LeftZone.Items.Count == 0)
        {
            LeftZone.Visibility = LeftSplitter.Visibility = Visibility.Collapsed;
            LeftColumn.Width = new GridLength(0);
        }

        if (RightZone.Items.Count == 0)
        {
            RightZone.Visibility = RightSplitter.Visibility = Visibility.Collapsed;
            RightColumn.Width = new GridLength(0);
        }

        if (BottomZone.Items.Count == 0)
        {
            BottomZone.Visibility = BottomSplitter.Visibility = Visibility.Collapsed;
            BottomRow.Height = new GridLength(0);
        }
    }
}
